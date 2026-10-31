using System.Collections.Concurrent;
using System.Security.Cryptography;
using _23DTHD5_Demo.Models;

namespace _23DTHD5_Demo.Services;

public class RoomState
{
    public object SyncRoot { get; } = new();
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Mode { get; set; } = "public";
    public string Password { get; set; } = string.Empty;
    public string Status { get; set; } = "waiting"; // "waiting" | "playing"
    public string Total { get; set; } = "10";
    public string Move { get; set; } = "30";
    public string Side { get; set; } = "random";
    public bool Spectators { get; set; } = true;
    public HashSet<string> PlayerIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SpectatorIds { get; } = new(StringComparer.Ordinal);
    public ConcurrentDictionary<string, DateTimeOffset> InviteTokens { get; } = new(StringComparer.Ordinal);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Ván cờ đang diễn ra (server giữ trạng thái duy nhất để hai bên luôn thấy cùng một bàn cờ)
    public XiangqiGame? Game { get; set; }
    public string? RedId { get; set; }
    public string? BlackId { get; set; }
    public string RedName { get; set; } = string.Empty;
    public string BlackName { get; set; } = string.Empty;
}

public class UserPresenceState
{
    public object SyncRoot { get; } = new();
    public string UserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public HashSet<string> ConnectionIds { get; } = new(StringComparer.Ordinal);
    public string? CurrentRoomId { get; set; }
}

public class RoomJoinResult
{
    public bool Succeeded { get; set; }
    public bool RequirePassword { get; set; }
    public int HttpStatus { get; set; } = 200;
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public string? Role { get; set; }
    public RoomDto? Room { get; set; }
}

public class RoomManagerService
{
    private readonly ConcurrentDictionary<string, RoomState> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UserPresenceState> _presence = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _connectionToUser = new(StringComparer.Ordinal);

    public RoomManagerService()
    {
        SeedDefaultRooms();
        // Khởi tạo trạng thái Online mặc định cho bạn bè Demo
        UserConnected("bot_online_kythanh", "Minh Quân (Bạn bè Demo)", "bot_conn_minhquan");
    }

    private void SeedDefaultRooms()
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Phòng công khai đang chờ (1/2 người) -> Vào thẳng bàn cờ
        var r1 = new RoomState
        {
            Id = "839201",
            Name = "Kỳ đài buổi tối",
            OwnerId = "seed_minhquan",
            Owner = "Minh Quân",
            Mode = "public",
            Password = "",
            Status = "waiting",
            Total = "10",
            Move = "30",
            Side = "random",
            Spectators = true,
            CreatedAt = now.AddMinutes(-5)
        };
        r1.PlayerIds.Add("seed_minhquan");
        _rooms[r1.Id] = r1;

        // 2. Phòng công khai đang thi đấu (2/2 người), cho phép xem -> Vào làm khán giả
        var r2 = new RoomState
        {
            Id = "482715",
            Name = "Thử sức cuối tuần",
            OwnerId = "seed_lananh",
            Owner = "Lan Anh",
            Mode = "public",
            Password = "",
            Status = "playing",
            Total = "15",
            Move = "60",
            Side = "red",
            Spectators = true,
            CreatedAt = now.AddMinutes(-10)
        };
        r2.PlayerIds.Add("seed_lananh");
        r2.PlayerIds.Add("seed_opponent_1");
        _rooms[r2.Id] = r2;

        // 3. Phòng riêng tư có mật khẩu (1234), đang chờ (1/2 người) -> Hiện popup mật khẩu
        var r3 = new RoomState
        {
            Id = "706324",
            Name = "Trận giao hữu",
            OwnerId = "seed_ducthanh",
            Owner = "Đức Thành",
            Mode = "private",
            Password = "1234",
            Status = "waiting",
            Total = "20",
            Move = "0",
            Side = "black",
            Spectators = false,
            CreatedAt = now.AddMinutes(-15)
        };
        r3.PlayerIds.Add("seed_ducthanh");
        _rooms[r3.Id] = r3;

        // 4. Phòng đã đủ 2 người chơi (waiting, 2/2) và không cho phép xem -> Test lỗi "Phòng đã đủ người chơi"
        var r4 = new RoomState
        {
            Id = "519402",
            Name = "Cao thủ tranh tài",
            OwnerId = "seed_hoangnam",
            Owner = "Hoàng Nam",
            Mode = "public",
            Password = "",
            Status = "waiting",
            Total = "15",
            Move = "30",
            Side = "red",
            Spectators = false,
            CreatedAt = now.AddMinutes(-20)
        };
        r4.PlayerIds.Add("seed_hoangnam");
        r4.PlayerIds.Add("seed_opponent_2");
        _rooms[r4.Id] = r4;

        // 5. Phòng đang trong ván đấu (playing, 2/2) và tắt chế độ xem -> Test lỗi "Phòng đang thi đấu, không cho phép khán giả theo dõi"
        var r5 = new RoomState
        {
            Id = "628190",
            Name = "Chung kết kín",
            OwnerId = "seed_baotram",
            Owner = "Bảo Trâm",
            Mode = "public",
            Password = "",
            Status = "playing",
            Total = "20",
            Move = "60",
            Side = "black",
            Spectators = false,
            CreatedAt = now.AddMinutes(-25)
        };
        r5.PlayerIds.Add("seed_baotram");
        r5.PlayerIds.Add("seed_opponent_3");
        _rooms[r5.Id] = r5;
    }

    public IReadOnlyList<RoomDto> GetAllRooms(string baseUrl)
    {
        return _rooms.Values
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToDto(r, baseUrl, includeInviteToken: false))
            .ToList();
    }

    public RoomState? GetRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId)) return null;
        _rooms.TryGetValue(roomId.Trim(), out var room);
        return room;
    }

    public (RoomDto Room, string InviteToken) CreateRoom(
        string ownerId,
        string ownerDisplayName,
        CreateRoomRequest request,
        string baseUrl)
    {
        var mode = request.Mode == "private" ? "private" : "public";
        var password = mode == "private" ? (request.Password?.Trim() ?? string.Empty) : string.Empty;

        string id;
        do
        {
            id = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        } while (_rooms.ContainsKey(id));

        var room = new RoomState
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"Phòng của {ownerDisplayName}" : request.Name.Trim(),
            OwnerId = ownerId,
            Owner = ownerDisplayName,
            Mode = mode,
            Password = password,
            Status = "waiting",
            Total = string.IsNullOrWhiteSpace(request.Total) ? "10" : request.Total,
            Move = string.IsNullOrWhiteSpace(request.Move) ? "30" : request.Move,
            Side = string.IsNullOrWhiteSpace(request.Side) ? "random" : request.Side,
            Spectators = request.Spectators,
            CreatedAt = DateTimeOffset.UtcNow
        };
        room.PlayerIds.Add(ownerId);

        var token = GenerateInviteToken(room);
        _rooms[room.Id] = room;
        SetUserRoom(ownerId, ownerDisplayName, room.Id);

        return (ToDto(room, baseUrl, includeInviteToken: true, token), token);
    }

    public string GenerateInviteToken(RoomState room)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        room.InviteTokens[token] = DateTimeOffset.UtcNow.AddHours(12);
        return token;
    }

    public bool ValidateInviteToken(RoomState room, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var clean = token.Trim();
        if (room.InviteTokens.TryGetValue(clean, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }
        // Also support base64 token format btoa(`${room.id}:${room.password}`) for backward compatibility
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(clean));
            if (decoded == $"{room.Id}:{room.Password}") return true;
        }
        catch
        {
            // Ignore malformed base64
        }
        return false;
    }

    public RoomJoinResult TryJoinRoom(
        string userId,
        string displayName,
        JoinRoomRequest request,
        string baseUrl)
    {
        var roomId = (request.RoomId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(roomId) || !_rooms.TryGetValue(roomId, out var room))
        {
            return new RoomJoinResult
            {
                Succeeded = false,
                HttpStatus = StatusCodes.Status404NotFound,
                ErrorCode = "NOT_FOUND",
                Message = "Không tìm thấy phòng"
            };
        }

        lock (room.SyncRoot)
        {
            var isExistingPlayer = room.PlayerIds.Contains(userId);
            var isOwner = room.OwnerId == userId;

            if (!isExistingPlayer)
            {
                // Edge case 1: Phòng đang trong ván đấu và tắt chế độ xem
                if (room.Status == "playing" && !room.Spectators)
                {
                    return new RoomJoinResult
                    {
                        Succeeded = false,
                        HttpStatus = StatusCodes.Status403Forbidden,
                        ErrorCode = "PLAYING_NO_SPECTATE",
                        Message = "Phòng đang thi đấu, không cho phép khán giả theo dõi"
                    };
                }

                // Edge case 2: Phòng đã đủ 2 người và không cho phép xem
                if (room.PlayerIds.Count >= 2 && !room.Spectators)
                {
                    return new RoomJoinResult
                    {
                        Succeeded = false,
                        HttpStatus = StatusCodes.Status403Forbidden,
                        ErrorCode = "ROOM_FULL",
                        Message = "Phòng đã đủ người chơi"
                    };
                }
            }

            // Kiểm tra mật khẩu nếu là phòng riêng tư có đặt mật khẩu
            var hasValidToken = ValidateInviteToken(room, request.InviteToken);
            if (room.Mode == "private" && !string.IsNullOrEmpty(room.Password) && !isOwner && !isExistingPlayer && !hasValidToken)
            {
                if (string.IsNullOrEmpty(request.Password))
                {
                    return new RoomJoinResult
                    {
                        Succeeded = false,
                        RequirePassword = true,
                        HttpStatus = StatusCodes.Status200OK,
                        ErrorCode = "PASSWORD_REQUIRED",
                        Message = "Vui lòng nhập mật khẩu phòng.",
                        Room = ToDto(room, baseUrl, includeInviteToken: false)
                    };
                }

                if (!string.Equals(request.Password.Trim(), room.Password, StringComparison.Ordinal))
                {
                    return new RoomJoinResult
                    {
                        Succeeded = false,
                        HttpStatus = StatusCodes.Status400BadRequest,
                        ErrorCode = "WRONG_PASSWORD",
                        Message = "Mật khẩu phòng không chính xác"
                    };
                }
            }

            // Xác định vai trò trong phòng
            string role;
            if (isOwner)
            {
                room.PlayerIds.Add(userId);
                role = "host";
            }
            else if (isExistingPlayer)
            {
                role = "player2";
            }
            else if (room.Status != "playing" && room.PlayerIds.Count < 2)
            {
                room.PlayerIds.Add(userId);
                role = room.PlayerIds.Count == 1 ? "host" : "player2";
            }
            else
            {
                room.SpectatorIds.Add(userId);
                role = "spectator";
            }

            // Nếu user đang ở phòng khác trước đó, rời phòng cũ trước khi vào phòng mới
            var (_, previousRoomId) = GetUserStatus(userId);
            if (!string.IsNullOrEmpty(previousRoomId) && previousRoomId != room.Id)
            {
                LeaveRoom(userId, previousRoomId);
            }

            SetUserRoom(userId, displayName, room.Id);
            var token = GenerateInviteToken(room);

            return new RoomJoinResult
            {
                Succeeded = true,
                HttpStatus = StatusCodes.Status200OK,
                Role = role,
                Room = ToDto(room, baseUrl, includeInviteToken: true, token)
            };
        }
    }

    // =========================================
    // Ván cờ (luật cờ tướng chạy ở server)
    // =========================================
    private string PlayerName(RoomState room, string userId)
    {
        if (userId == room.OwnerId && !string.IsNullOrWhiteSpace(room.Owner)) return room.Owner;
        if (_presence.TryGetValue(userId, out var state) && !string.IsNullOrWhiteSpace(state.DisplayName))
            return state.DisplayName;
        return userId;
    }

    /// <summary>Tạo ván mới khi phòng đủ 2 người chơi (hoặc khi ván cũ đã kết thúc và người chơi đã thay đổi).</summary>
    public bool EnsureGame(RoomState room)
    {
        lock (room.SyncRoot)
        {
            if (room.PlayerIds.Count < 2) return false;
            if (room.Game is not null)
            {
                var playersChanged = room.Game.IsFinished
                    && (room.RedId is null || room.BlackId is null
                        || !room.PlayerIds.Contains(room.RedId) || !room.PlayerIds.Contains(room.BlackId));
                if (!playersChanged) return false;
            }

            var ownerId = room.PlayerIds.Contains(room.OwnerId) ? room.OwnerId : room.PlayerIds.First();
            var otherId = room.PlayerIds.First(id => id != ownerId);
            var ownerIsRed = room.Side switch
            {
                "red" => true,
                "black" => false,
                _ => RandomNumberGenerator.GetInt32(2) == 0
            };
            StartGame(room, ownerIsRed ? ownerId : otherId, ownerIsRed ? otherId : ownerId);
            return true;
        }
    }

    private void StartGame(RoomState room, string redId, string blackId)
    {
        room.Game = new XiangqiGame();
        room.RedId = redId;
        room.BlackId = blackId;
        room.RedName = PlayerName(room, redId);
        room.BlackName = PlayerName(room, blackId);
        room.Status = "playing";
    }

    /// <summary>Chơi lại: đổi phe, chỉ người chơi trong phòng được yêu cầu và chỉ khi ván đã kết thúc.</summary>
    public (bool Ok, string? Error) Rematch(RoomState room, string userId)
    {
        lock (room.SyncRoot)
        {
            if (room.Game is null || !room.Game.IsFinished) return (false, "Ván cờ chưa kết thúc.");
            if (userId != room.RedId && userId != room.BlackId) return (false, "Chỉ người chơi mới được bắt đầu ván mới.");
            if (room.RedId is null || room.BlackId is null
                || !room.PlayerIds.Contains(room.RedId) || !room.PlayerIds.Contains(room.BlackId))
                return (false, "Phòng chưa đủ hai người chơi.");
            StartGame(room, room.BlackId, room.RedId);
            return (true, null);
        }
    }

    public GameStateDto? GetGameState(RoomState room)
    {
        lock (room.SyncRoot)
        {
            return room.Game is null ? null : BuildState(room);
        }
    }

    private static GameStateDto BuildState(RoomState room)
    {
        var game = room.Game!;
        return new GameStateDto
        {
            RoomId = room.Id,
            Pieces = game.Pieces.ToList(),
            Turn = game.Turn,
            InCheck = game.InCheck,
            Finished = game.IsFinished,
            Winner = game.Winner,
            Reason = game.EndReason,
            MoveNumber = game.MoveNumber,
            LastMove = game.LastMove,
            RedId = room.RedId,
            BlackId = room.BlackId,
            RedName = room.RedName,
            BlackName = room.BlackName
        };
    }

    public (bool Ok, string? Error, GameStateDto? State) TryMove(
        RoomState room, string userId, string pieceId, int toRow, int toCol)
    {
        lock (room.SyncRoot)
        {
            if (room.Game is null) return (false, "Ván cờ chưa bắt đầu. Cần đủ 2 người chơi.", null);
            var color = userId == room.RedId ? XiangqiGame.Red
                : userId == room.BlackId ? XiangqiGame.Black
                : string.Empty;
            var result = room.Game.TryMove(color, pieceId, toRow, toCol);
            return (result.Ok, result.Error, BuildState(room));
        }
    }

    public List<int[]> GetLegalMoves(RoomState room, string userId, string pieceId)
    {
        lock (room.SyncRoot)
        {
            var game = room.Game;
            var result = new List<int[]>();
            if (game is null || game.IsFinished) return result;
            var color = userId == room.RedId ? XiangqiGame.Red : userId == room.BlackId ? XiangqiGame.Black : string.Empty;
            var piece = game.GetPiece(pieceId);
            if (piece is null || piece.Color != color || game.Turn != color) return result;
            foreach (var (row, col) in game.LegalMoves(piece)) result.Add([row, col]);
            return result;
        }
    }

    public void LeaveRoom(string userId, string roomId)
    {
        if (_rooms.TryGetValue(roomId, out var room))
        {
            lock (room.SyncRoot)
            {
                // Rời phòng giữa ván: đối thủ thắng
                if (room.Game is { IsFinished: false } game)
                {
                    if (userId == room.RedId) game.EndGame(XiangqiGame.Black, "opponent_left");
                    else if (userId == room.BlackId) game.EndGame(XiangqiGame.Red, "opponent_left");
                    if (game.IsFinished && !room.OwnerId.StartsWith("seed_", StringComparison.Ordinal))
                        room.Status = "waiting";
                }

                // Keep seed rooms intact so test cases stay repeatable
                if (!room.OwnerId.StartsWith("seed_", StringComparison.Ordinal))
                {
                    room.PlayerIds.Remove(userId);
                    if (userId == room.OwnerId && room.PlayerIds.Remove("bot_online_kythanh"))
                    {
                        SetUserRoom("bot_online_kythanh", "Minh Quân (Bạn bè Demo)", null);
                    }
                }
                else if (userId != room.OwnerId && !userId.StartsWith("seed_", StringComparison.Ordinal))
                {
                    room.PlayerIds.Remove(userId);
                }
                room.SpectatorIds.Remove(userId);
            }
        }

        if (_presence.TryGetValue(userId, out var state))
        {
            lock (state.SyncRoot)
            {
                if (state.CurrentRoomId == roomId)
                {
                    state.CurrentRoomId = null;
                }
            }
        }
    }

    public void UserConnected(string userId, string displayName, string connectionId)
    {
        _connectionToUser[connectionId] = userId;
        var state = _presence.GetOrAdd(userId, _ => new UserPresenceState
        {
            UserId = userId,
            DisplayName = displayName
        });

        lock (state.SyncRoot)
        {
            state.DisplayName = displayName;
            state.ConnectionIds.Add(connectionId);
            if (state.ConnectionIds.Count == 1)
            {
                state.CurrentRoomId = null;
            }
        }
    }

    public void SetUserAtLobby(string userId, string displayName)
    {
        var state = _presence.GetOrAdd(userId, _ => new UserPresenceState
        {
            UserId = userId,
            DisplayName = displayName
        });

        string? previousRoomId;
        lock (state.SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                state.DisplayName = displayName;
            }
            previousRoomId = state.CurrentRoomId;
            state.CurrentRoomId = null;
        }

        if (!string.IsNullOrEmpty(previousRoomId))
        {
            LeaveRoom(userId, previousRoomId);
        }
    }

    public void UserDisconnected(string connectionId)
    {
        if (!_connectionToUser.TryRemove(connectionId, out var userId)) return;
        if (_presence.TryGetValue(userId, out var state))
        {
            lock (state.SyncRoot)
            {
                state.ConnectionIds.Remove(connectionId);
                if (state.ConnectionIds.Count == 0)
                {
                    state.CurrentRoomId = null;
                }
            }
        }
    }

    public void SetUserRoom(string userId, string displayName, string? roomId)
    {
        var state = _presence.GetOrAdd(userId, _ => new UserPresenceState
        {
            UserId = userId,
            DisplayName = displayName
        });

        lock (state.SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                state.DisplayName = displayName;
            }
            state.CurrentRoomId = roomId;
        }
    }

    public (string Status, string? CurrentRoomId) GetUserStatus(string userId)
    {
        if (_presence.TryGetValue(userId, out var state))
        {
            lock (state.SyncRoot)
            {
                if (state.ConnectionIds.Count > 0)
                {
                    return string.IsNullOrEmpty(state.CurrentRoomId)
                        ? ("Online", null)
                        : ("In-game", state.CurrentRoomId);
                }
            }
        }
        return ("Offline", null);
    }

    public RoomDto ToDto(RoomState room, string baseUrl, bool includeInviteToken, string? token = null)
    {
        lock (room.SyncRoot)
        {
            var inviteToken = includeInviteToken ? (token ?? GenerateInviteToken(room)) : null;
            var querySuffix = !string.IsNullOrEmpty(inviteToken) && room.Mode == "private" && !string.IsNullOrEmpty(room.Password)
                ? $"?token={Uri.EscapeDataString(inviteToken)}"
                : string.Empty;
            var deepLink = $"cotuong://room/{room.Id}{querySuffix}";
            var shareUrl = !string.IsNullOrEmpty(inviteToken) && room.Mode == "private" && !string.IsNullOrEmpty(room.Password)
                ? $"{baseUrl.TrimEnd('/')}/room/{room.Id}?deeplink={Uri.EscapeDataString(deepLink)}&token={Uri.EscapeDataString(inviteToken)}"
                : $"{baseUrl.TrimEnd('/')}/room/{room.Id}?deeplink={Uri.EscapeDataString(deepLink)}";

            return new RoomDto
            {
                Id = room.Id,
                Name = room.Name,
                OwnerId = room.OwnerId,
                Owner = room.Owner,
                Mode = room.Mode,
                HasPassword = room.Mode == "private" && !string.IsNullOrEmpty(room.Password),
                Status = room.Status,
                PlayerCount = room.PlayerIds.Count,
                MaxPlayers = 2,
                Total = room.Total,
                Move = room.Move,
                Side = room.Side,
                Spectators = room.Spectators,
                DeepLink = deepLink,
                ShareUrl = shareUrl
            };
        }
    }
}
