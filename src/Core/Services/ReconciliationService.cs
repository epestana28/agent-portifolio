using AgentPortifolio.Core.Domain;
using AgentPortifolio.Core.Ports;

namespace AgentPortifolio.Core.Services;

public sealed record DriftFinding(string Kind, Guid PrincipalObjectId, string Type, string Resource, bool Dangerous);

public sealed record ReconciliationReport(Guid AgentId, IReadOnlyList<DriftFinding> Findings, bool Suspended, CoverageMode Coverage);

/// <summary>
/// Compares what the external source of truth grants with what the wallet
/// expects. Events can be lost or late, so this runs periodically as well.
/// Effective access is the intersection of wallet and external authority, so
/// any external grant the wallet does not account for is a finding.
/// </summary>
public sealed class ReconciliationService(IWalletStore store, IExternalGrantSource source, TimeProvider time)
{
    /// <summary>
    /// Tenant-wide permission names that defeat resource scoping. Not exhaustive:
    /// the permission matrix lists the ones reviewed against the documentation.
    /// </summary>
    public static readonly IReadOnlySet<string> BroadGrantTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Sites.Read.All", "Sites.ReadWrite.All", "Sites.Manage.All", "Sites.FullControl.All",
        "Files.Read.All", "Files.ReadWrite.All", "Mail.Read", "Mail.ReadWrite", "Mail.Send",
    };

    public async Task<ReconciliationReport> ReconcileAsync(Guid tenantId, Guid agentId, string correlationId, CancellationToken ct)
    {
        var agent = await store.TransactAsync(tx => tx.GetAgentAsync(tenantId, agentId), ct) ?? throw new NotFoundException("Agent not found.");

        // External reads happen outside the transaction: no lock is held across the network.
        var observedExecutor = await source.ListAsync(tenantId, agent.ExecutorObjectId, ct);
        var observedAgent = await source.ListAsync(tenantId, agent.Identity.ObjectId, ct);

        return await store.TransactAsync(async tx =>
        {
            var now = time.GetUtcNow();
            var current = await tx.GetAgentAsync(tenantId, agentId) ?? throw new NotFoundException("Agent not found.");
            var recorded = await tx.ListGrantsAsync(tenantId, agentId);
            var capabilities = await tx.ListCapabilitiesAsync(tenantId, agentId);
            var allowedResources = capabilities.Where(c => c.RevokedAt is null).Select(c => c.Resource).ToHashSet(StringComparer.Ordinal);
            var findings = new List<DriftFinding>();

            foreach (var (principal, observed) in new[] { (current.ExecutorObjectId, observedExecutor), (current.Identity.ObjectId, observedAgent) })
            {
                foreach (var grant in observed)
                {
                    var known = recorded.FirstOrDefault(g => g.PrincipalObjectId == principal && g.ExternalId == grant.ExternalId);
                    var broad = BroadGrantTypes.Contains(grant.Type);
                    var direct = principal == current.Identity.ObjectId;
                    var unexpected = known is null || !allowedResources.Contains(grant.Resource);

                    if (broad)
                    {
                        findings.Add(new DriftFinding("broad_grant", principal, grant.Type, grant.Resource, Dangerous: true));
                    }
                    else if (direct)
                    {
                        findings.Add(new DriftFinding("direct_access", principal, grant.Type, grant.Resource, Dangerous: false));
                    }
                    else if (unexpected)
                    {
                        findings.Add(new DriftFinding("unexpected_grant", principal, grant.Type, grant.Resource, Dangerous: false));
                    }

                    await tx.UpsertGrantAsync(new ExternalGrant(known?.Id ?? Guid.NewGuid(), tenantId, agentId, principal, grant.Type,
                        grant.ExternalId, grant.Resource, grant.Role, "reconciliation", now, "present"));
                }

                foreach (var gone in recorded.Where(g => g.PrincipalObjectId == principal && g.LastResult == "present"
                    && observed.All(o => o.ExternalId != g.ExternalId)))
                {
                    await tx.UpsertGrantAsync(gone with { LastVerifiedAt = now, LastResult = "absent" });
                    await tx.AppendAuditAsync(Audit.Create(now, tenantId, agentId, EventTypes.GrantAbsent, correlationId, AccessModes.System,
                        resource: gone.Resource, evidenceRef: $"grant:{gone.ExternalId}"));
                }
            }

            foreach (var finding in findings)
            {
                var type = finding.Kind switch
                {
                    "broad_grant" => EventTypes.AlertBroadGrant,
                    "direct_access" => EventTypes.AlertDirectAccess,
                    _ => EventTypes.AlertDrift,
                };
                await tx.AppendAuditAsync(Audit.Create(now, tenantId, agentId, type, correlationId, AccessModes.System,
                    revocationEpoch: current.RevocationEpoch, resource: finding.Resource, reasonCode: finding.Kind, evidenceRef: $"grant-type:{finding.Type}"));
            }

            // Coverage is only as good as the last check. A direct path lowers it until removed.
            var hasDirectPath = findings.Any(f => f.Kind is "direct_access" or "broad_grant");
            var mode = hasDirectPath ? CoverageMode.DirectAccessPossible : CoverageMode.GatewayEnforced;
            if (current.Coverage.Mode != mode)
            {
                await tx.AppendAuditAsync(Audit.Create(now, tenantId, agentId, EventTypes.CoverageChanged, correlationId, AccessModes.System,
                    reasonCode: mode.ToString()));
            }

            current = await tx.UpdateAgentAsync(current with { Coverage = new Coverage(mode, !hasDirectPath, now) });

            var suspended = false;
            if (findings.Any(f => f.Dangerous) && current.State == AgentState.Active)
            {
                await ControlService.SuspendInTransactionAsync(tx, now, current, initiator: null, "Dangerous drift: broad external grant detected.", correlationId);
                suspended = true;
            }

            return new ReconciliationReport(agentId, findings, suspended, mode);
        }, ct);
    }
}
