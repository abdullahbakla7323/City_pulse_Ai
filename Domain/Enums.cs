namespace CityPulseAI.Domain;

public enum CrewStatus
{
    Idle,
    OnWay,
    Busy
}

public enum CrewType
{
    Sanitation,
    Zoning,
    PublicWorks,
    Parks
}

public enum UserRole
{
    Admin,
    Dispatcher,
    Citizen
}
