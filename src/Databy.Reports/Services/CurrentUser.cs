namespace Databy.Reports.Services;

public interface ICurrentUser
{
    string Name { get; }
}

/// <summary>
/// Пока локальная авторизация не реализована, пользователем считается учётная запись ОС.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    public string Name
    {
        get
        {
            var user = Environment.UserName;
            if (string.IsNullOrWhiteSpace(user)) return "unknown";
            return OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(Environment.UserDomainName)
                ? $"{Environment.UserDomainName}\\{user}"
                : user;
        }
    }
}