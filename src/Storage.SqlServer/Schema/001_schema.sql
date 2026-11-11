-- Agent Portifolio schema for SQL Server 2022 and Azure SQL Database.
-- Idempotent. Creating the ledger table requires the ENABLE LEDGER permission.
-- Verification procedures need ALLOW_SNAPSHOT_ISOLATION on the database (see runbooks).

IF SCHEMA_ID(N'ap') IS NULL EXEC(N'CREATE SCHEMA ap');

IF OBJECT_ID(N'ap.Agent') IS NULL
CREATE TABLE ap.Agent
(
    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Agent PRIMARY KEY,
    TenantId         UNIQUEIDENTIFIER NOT NULL,
    IdentityObjectId UNIQUEIDENTIFIER NOT NULL,
    State            NVARCHAR(32)     NOT NULL,
    RevocationEpoch  BIGINT           NOT NULL,
    Version          BIGINT           NOT NULL,
    Doc              NVARCHAR(MAX)    NOT NULL,
    CONSTRAINT UQ_Agent_Identity UNIQUE (TenantId, IdentityObjectId)
);

IF OBJECT_ID(N'ap.Capability') IS NULL
CREATE TABLE ap.Capability
(
    Id       UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Capability PRIMARY KEY,
    TenantId UNIQUEIDENTIFIER NOT NULL,
    AgentId  UNIQUEIDENTIFIER NOT NULL,
    IssuedAt DATETIMEOFFSET   NOT NULL,
    Doc      NVARCHAR(MAX)    NOT NULL,
    INDEX IX_Capability_Agent (TenantId, AgentId)
);

IF OBJECT_ID(N'ap.ExternalGrant') IS NULL
CREATE TABLE ap.ExternalGrant
(
    Id         UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ExternalGrant PRIMARY KEY,
    TenantId   UNIQUEIDENTIFIER NOT NULL,
    AgentId    UNIQUEIDENTIFIER NOT NULL,
    ExternalId NVARCHAR(256)    NOT NULL,
    Doc        NVARCHAR(MAX)    NOT NULL,
    INDEX IX_ExternalGrant_Agent (TenantId, AgentId)
);

IF OBJECT_ID(N'ap.Action') IS NULL
CREATE TABLE ap.Action
(
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Action PRIMARY KEY,
    TenantId       UNIQUEIDENTIFIER NOT NULL,
    AgentId        UNIQUEIDENTIFIER NOT NULL,
    IdempotencyKey NVARCHAR(128)    NOT NULL,
    AdmittedAt     DATETIMEOFFSET   NOT NULL,
    State          NVARCHAR(32)     NOT NULL,
    Doc            NVARCHAR(MAX)    NOT NULL,
    CONSTRAINT UQ_Action_Idempotency UNIQUE (TenantId, AgentId, IdempotencyKey),
    INDEX IX_Action_Agent (TenantId, AgentId, AdmittedAt)
);

IF OBJECT_ID(N'ap.ContainmentOperation') IS NULL
CREATE TABLE ap.ContainmentOperation
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ContainmentOperation PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL,
    AgentId     UNIQUEIDENTIFIER NOT NULL,
    RequestedAt DATETIMEOFFSET   NOT NULL,
    EpochAfter  BIGINT           NOT NULL,
    Doc         NVARCHAR(MAX)    NOT NULL,
    INDEX IX_Containment_Agent (TenantId, AgentId, RequestedAt)
);

IF OBJECT_ID(N'ap.Outbox') IS NULL
CREATE TABLE ap.Outbox
(
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Outbox PRIMARY KEY,
    TenantId    UNIQUEIDENTIFIER NOT NULL,
    CreatedAt   DATETIMEOFFSET   NOT NULL,
    Epoch       BIGINT           NOT NULL,
    ProcessedAt DATETIMEOFFSET   NULL,
    Doc         NVARCHAR(MAX)    NOT NULL,
    INDEX IX_Outbox_Pending (ProcessedAt, CreatedAt)
);

-- Evidence. Append-only ledger table: INSERT only, UPDATE and DELETE are rejected
-- by the engine. The ledger gives tamper evidence on verification; it does not stop
-- a false first entry and it is not a backup.
IF OBJECT_ID(N'ap.AuditEvent') IS NULL
CREATE TABLE ap.AuditEvent
(
    EventId          UNIQUEIDENTIFIER NOT NULL,
    SchemaVersion    INT              NOT NULL,
    TenantId         UNIQUEIDENTIFIER NOT NULL,
    AgentId          UNIQUEIDENTIFIER NULL,
    OccurredAt       DATETIMEOFFSET   NOT NULL,
    RecordedAt       DATETIMEOFFSET   NOT NULL,
    EventType        NVARCHAR(64)     NOT NULL,
    CorrelationId    NVARCHAR(128)    NOT NULL,
    HumanInitiator   UNIQUEIDENTIFIER NULL,
    CallerIdentity   UNIQUEIDENTIFIER NULL,
    ExecutorIdentity UNIQUEIDENTIFIER NULL,
    AccessMode       NVARCHAR(16)     NOT NULL,
    PolicyVersion    INT              NULL,
    RevocationEpoch  BIGINT           NULL,
    CapabilityId     UNIQUEIDENTIFIER NULL,
    Resource         NVARCHAR(1024)   NULL,
    Operation        NVARCHAR(64)     NULL,
    Decision         NVARCHAR(16)     NULL,
    ReasonCode       NVARCHAR(64)     NULL,
    ExecutionState   NVARCHAR(32)     NULL,
    Attempt          INT              NULL,
    IdempotencyKey   NVARCHAR(128)    NULL,
    EvidenceRef      NVARCHAR(256)    NULL
)
WITH (LEDGER = ON (APPEND_ONLY = ON));
