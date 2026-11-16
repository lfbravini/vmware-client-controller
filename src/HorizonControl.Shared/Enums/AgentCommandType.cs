namespace HorizonControl.Shared.Enums;

public enum AgentCommandType
{
    RestartClientInstance = 0,
    KillClientInstance = 1,
    FlushLocalCache = 2,
    StartApp = 3,
    RebootGraceful = 4,
    RebootForce = 5,
    Shutdown = 6,
    LockWorkstation = 7,
    StartRemoteAssist = 8
}
