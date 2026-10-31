using System.Security.Claims;
using System.Text.RegularExpressions;
using _23DTHD5_Demo.Data;
using _23DTHD5_Demo.Hubs;
using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD5_Demo.Controllers.api;

[Authorize]
[ApiController]
[Route("api/rooms")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RoomController(
    RoomManagerService roomManager,
    ApplicationDbContext db,
    IHubContext<ChessHub> hubContext) : ControllerBase
{
    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
    private string CurrentDisplayName =>
        User.FindFirstValue("display_name") ?? User.Identity?.Name ?? "Kỳ thủ";
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet]
    public IActionResult GetRooms()
    {
        return Ok(new { rooms = roomManager.GetAllRooms(BaseUrl) });
    }

    [HttpGet("friends")]
    public async Task<IActionResult> GetFriends()
    {
        var myId = CurrentUserId;
        var dbUsers = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != myId)
            .OrderByDescending(u => u.CreatedAt)
            .Take(50)
            .Select(u => new { u.Id, u.UserName, u.DisplayName })
            .ToListAsync();

        var friends = new List<FriendDto>();
        foreach (var user in dbUsers)
        {
            var (status, currentRoomId) = roomManager.GetUserStatus(user.Id);
            friends.Add(new FriendDto
            {
                Id = user.Id,
                Username = user.UserName ?? "kythu",
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.UserName ?? "Kỳ thủ") : user.DisplayName,
                Status = status,
                CurrentRoomId = currentRoomId,
                IsDemoBot = false
            });
        }

        // Sắp xếp người chơi thật: Online lên đầu -> In-game -> Offline
        friends = friends
            .OrderBy(f => f.Status == "Online" ? 0 : f.Status == "In-game" ? 1 : 2)
            .ThenBy(f => f.DisplayName)
            .ToList();

        // Bổ sung 3 bạn bè mẫu (Demo) để luôn có đủ 3 trạng thái Online / In-game / Offline khi kiểm thử 1 máy
        var (botOnlineStatus, botOnlineRoomId) = roomManager.GetUserStatus("bot_online_kythanh");
        friends.AddRange(
        [
            new FriendDto
            {
                Id = "bot_online_kythanh",
                Username = "minhquan_pro",
                DisplayName = "Minh Quân (Bạn bè Demo)",
                Status = botOnlineStatus,
                CurrentRoomId = botOnlineRoomId,
                IsDemoBot = true
            },
            new FriendDto
            {
                Id = "bot_ingame_lananh",
                Username = "lananh_chess",
                DisplayName = "Lan Anh (Đang đấu)",
                Status = "In-game",
                CurrentRoomId = "482715",
                IsDemoBot = true
            },
            new FriendDto
            {
                Id = "bot_offline_ducthanh",
                Username = "ducthanh99",
                DisplayName = "Đức Thành",
                Status = "Offline",
                CurrentRoomId = null,
                IsDemoBot = true
            }
        ]);

        return Ok(new { friends });
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoom(CreateRoomRequest request)
    {
        var mode = request.Mode == "private" ? "private" : "public";
        var password = (request.Password ?? string.Empty).Trim();
        if (mode == "private" && !Regex.IsMatch(password, "^[A-Za-z0-9]{4,6}$"))
        {
            return BadRequest(new { message = "Phòng riêng tư cần mật khẩu gồm 4–6 chữ số hoặc ký tự, không dấu cách." });
        }

        var (room, inviteToken) = roomManager.CreateRoom(CurrentUserId, CurrentDisplayName, request, BaseUrl);
        await hubContext.Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl));
        await hubContext.Clients.All.SendAsync("PresenceUpdated");

        return Ok(new
        {
            room,
            inviteToken,
            role = "host"
        });
    }

    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom(JoinRoomRequest request)
    {
        var result = roomManager.TryJoinRoom(CurrentUserId, CurrentDisplayName, request, BaseUrl);
        if (result.RequirePassword)
        {
            return Ok(new
            {
                requirePassword = true,
                room = result.Room,
                message = result.Message
            });
        }

        if (!result.Succeeded)
        {
            return StatusCode(result.HttpStatus, new
            {
                code = result.ErrorCode,
                message = result.Message
            });
        }

        var joinedRoom = roomManager.GetRoom(request.RoomId);
        if (joinedRoom is not null && roomManager.EnsureGame(joinedRoom))
        {
            var state = roomManager.GetGameState(joinedRoom);
            if (state is not null)
            {
                await hubContext.Clients.Group(joinedRoom.Id).SendAsync("GameState", state);
            }
        }

        await hubContext.Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl));
        await hubContext.Clients.All.SendAsync("PresenceUpdated");

        return Ok(new
        {
            succeeded = true,
            role = result.Role,
            room = result.Room
        });
    }

    [HttpPost("leave/{id}")]
    public async Task<IActionResult> LeaveRoom(string id)
    {
        roomManager.LeaveRoom(CurrentUserId, id);
        var leftRoom = roomManager.GetRoom(id);
        var leftState = leftRoom is null ? null : roomManager.GetGameState(leftRoom);
        if (leftState is not null)
        {
            await hubContext.Clients.Group(id).SendAsync("GameState", leftState);
        }
        await hubContext.Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl));
        await hubContext.Clients.All.SendAsync("PresenceUpdated");
        return Ok(new { message = "Đã rời phòng." });
    }

    [HttpPost("lobby")]
    public async Task<IActionResult> EnterLobby()
    {
        roomManager.SetUserAtLobby(CurrentUserId, CurrentDisplayName);
        await hubContext.Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl));
        await hubContext.Clients.All.SendAsync("PresenceUpdated");
        return Ok(new { status = "Online" });
    }
}
