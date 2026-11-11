using AgentPortifolio.Hosting;

// DEVELOPMENT ONLY. Mints a token for the local stack, signed with the shared
// development key. Hosts in Entra mode never accept these tokens.
//
// usage: devtoken <control|executor> <tenant-guid> <object-guid> [role,role...]

if (args.Length < 3 || !Guid.TryParse(args[1], out var tenant) || !Guid.TryParse(args[2], out var objectId))
{
    Console.Error.WriteLine("usage: devtoken <control|executor> <tenant-guid> <object-guid> [role,role...]");
    return 2;
}

var audience = args[0] switch
{
    "control" => Audiences.Control,
    "executor" => Audiences.Executor,
    _ => null,
};
var key = Environment.GetEnvironmentVariable("Auth__DevSigningKey");
if (audience is null || string.IsNullOrEmpty(key))
{
    Console.Error.WriteLine("The audience must be control or executor, and Auth__DevSigningKey must be set.");
    return 2;
}

var roles = args.Length > 3 ? args[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
Console.WriteLine(DevTokens.Create(key, audience, tenant, objectId, roles, TimeSpan.FromMinutes(30)));
return 0;
