namespace _23DTHD5_Demo.Models;

public class CreateRoomRequest
{
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "public";
    public string? Password { get; set; }
    public string Total { get; set; } = "10";
    public string Move { get; set; } = "30";
    public string Side { get; set; } = "random";
    public bool Spectators { get; set; } = true;
}

public class JoinRoomRequest
{
    public string RoomId { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? InviteToken { get; set; }
}

public class RoomDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Mode { get; set; } = "public";
    public bool HasPassword { get; set; }
    public string Status { get; set; } = "waiting";
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; } = 2;
    public string Total { get; set; } = "10";
    public string Move { get; set; } = "30";
    public string Side { get; set; } = "random";
    public bool Spectators { get; set; } = true;
    public string DeepLink { get; set; } = string.Empty;
    public string ShareUrl { get; set; } = string.Empty;
}

public class FriendDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = "Offline"; // Online, In-game, Offline
    public string? CurrentRoomId { get; set; }
    public bool IsDemoBot { get; set; }
}
