using System.Security.Claims;
using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace _23DTHD5_Demo.Hubs
{
    [Authorize]
    public class ChessHub(RoomManagerService roomManager) : Hub
    {
        private string CurrentUserId =>
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        private string CurrentDisplayName =>
            Context.User?.FindFirst("display_name")?.Value
            ?? Context.User?.Identity?.Name
            ?? "Kỳ thủ";

        public override async Task OnConnectedAsync()
        {
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                roomManager.UserConnected(CurrentUserId, CurrentDisplayName, Context.ConnectionId);
                await Clients.All.SendAsync("PresenceUpdated");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            roomManager.UserDisconnected(Context.ConnectionId);
            await Clients.All.SendAsync("PresenceUpdated");
            await base.OnDisconnectedAsync(exception);
        }

        // =========================================
        // Người chơi ở Sảnh chờ (Online)
        // =========================================
        public async Task EnterLobby()
        {
            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                roomManager.SetUserAtLobby(CurrentUserId, CurrentDisplayName);
                await Clients.All.SendAsync("PresenceUpdated");
            }
        }

        // =========================================
        // Người chơi tham gia room
        // =========================================
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                roomId
            );

            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                roomManager.SetUserRoom(CurrentUserId, CurrentDisplayName, roomId);
                var joinedRoom = roomManager.GetRoom(roomId);
                if (joinedRoom is not null && roomManager.EnsureGame(joinedRoom))
                {
                    await Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl()));
                }
                await BroadcastGameState(roomId);
                await Clients.All.SendAsync("PresenceUpdated");
            }

            Console.WriteLine(
                $"{Context.ConnectionId} ({CurrentDisplayName}) joined room {roomId}"
            );
        }

        // =========================================
        // Người chơi rời room
        // =========================================
        public async Task LeaveRoom(string roomId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                roomId
            );

            if (!string.IsNullOrEmpty(CurrentUserId))
            {
                roomManager.LeaveRoom(CurrentUserId, roomId);
                await BroadcastGameState(roomId);
                await Clients.All.SendAsync("PresenceUpdated");
            }

            Console.WriteLine(
                $"{Context.ConnectionId} ({CurrentDisplayName}) left room {roomId}"
            );
        }

        // =========================================
        // Gửi lời mời bạn bè trong game (Real-time)
        // =========================================
        public async Task SendRoomInvite(string targetUserId, string roomId)
        {
            if (string.IsNullOrWhiteSpace(targetUserId) || string.IsNullOrWhiteSpace(roomId))
                return;

            var room = roomManager.GetRoom(roomId);
            if (room is null)
                return;

            var hostName = CurrentDisplayName;
            var hostId = CurrentUserId;
            var inviteToken = roomManager.GenerateInviteToken(room);

            // Nếu mời bạn bè Demo (bot_online_kythanh) khi test 1 máy -> Giả lập phản hồi Chấp nhận & thêm thật vào phòng
            if (targetUserId == "bot_online_kythanh")
            {
                var baseUrl = $"{Context.GetHttpContext()?.Request.Scheme ?? "http"}://{Context.GetHttpContext()?.Request.Host.Value ?? "localhost:5275"}";
                var joinResult = roomManager.TryJoinRoom(
                    "bot_online_kythanh",
                    "Minh Quân (Bạn bè Demo)",
                    new JoinRoomRequest
                    {
                        RoomId = room.Id,
                        InviteToken = inviteToken
                    },
                    baseUrl
                );

                if (!joinResult.Succeeded)
                {
                    await Clients.Caller.SendAsync(
                        "InviteResponse",
                        new
                        {
                            friendName = "Minh Quân (Bạn bè Demo)",
                            roomId = room.Id,
                            accepted = false,
                            message = $"Không thể thêm Minh Quân vào phòng: {joinResult.Message}"
                        }
                    );
                    return;
                }

                roomManager.EnsureGame(room);
                await BroadcastGameState(room.Id);
                await Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(baseUrl));
                await Clients.All.SendAsync("PresenceUpdated");

                await Clients.Caller.SendAsync(
                    "InviteResponse",
                    new
                    {
                        friendName = "Minh Quân (Bạn bè Demo)",
                        roomId = room.Id,
                        accepted = true,
                        message = "Minh Quân (Bạn bè Demo) đã chấp nhận lời mời và vào phòng (2/2)!"
                    }
                );

                await Clients.Group(room.Id).SendAsync(
                    "ReceiveMessage",
                    "Hệ thống",
                    "Minh Quân (Bạn bè Demo) đã chấp nhận lời mời vào phòng."
                );
                return;
            }

            // Gửi thông báo real-time tới thiết bị người bạn
            await Clients.User(targetUserId).SendAsync(
                "ReceiveRoomInvite",
                new
                {
                    hostId,
                    hostName,
                    roomId = room.Id,
                    roomName = room.Name,
                    inviteToken,
                    message = $"{hostName} mời bạn vào phòng {room.Id}."
                }
            );
        }

        // =========================================
        // Phản hồi lời mời (Chấp nhận / Từ chối)
        // =========================================
        public async Task RespondRoomInvite(string hostUserId, string roomId, bool accepted)
        {
            if (string.IsNullOrWhiteSpace(hostUserId))
                return;

            var friendName = CurrentDisplayName;
            var statusText = accepted
                ? $"{friendName} đã chấp nhận lời mời vào phòng {roomId}."
                : $"{friendName} đã từ chối lời mời vào phòng {roomId}.";

            await Clients.User(hostUserId).SendAsync(
                "InviteResponse",
                new
                {
                    friendName,
                    roomId,
                    accepted,
                    message = statusText
                }
            );

            if (accepted && !string.IsNullOrWhiteSpace(roomId))
            {
                await Clients.Group(roomId).SendAsync(
                    "ReceiveMessage",
                    "Hệ thống",
                    $"{friendName} đã tham gia phòng qua lời mời."
                );
            }
        }

        // =========================================
        // Nhận nước đi
        // =========================================
        public string GetMyUserId() => CurrentUserId;

        // Trạng thái ván cờ hiện tại của phòng (dùng khi vào phòng / tải lại trang)
        public GameStateDto? GetGameState(string roomId)
        {
            var room = roomManager.GetRoom(roomId);
            return room is null ? null : roomManager.GetGameState(room);
        }

        // Các ô đi hợp lệ của một quân (chỉ trả về cho đúng người đang đến lượt)
        public List<int[]> GetLegalMoves(string roomId, string pieceId)
        {
            var room = roomManager.GetRoom(roomId);
            return room is null ? [] : roomManager.GetLegalMoves(room, CurrentUserId, pieceId);
        }

        // =========================================
        // Nhận nước đi: server kiểm tra luật, lượt đi, chủ quân rồi mới phát cho cả phòng
        // =========================================
        public async Task MovePiece(
            string roomId,
            string pieceId,
            int fromRow,
            int fromCol,
            int toRow,
            int toCol)
        {
            var room = roomManager.GetRoom(roomId);
            if (room is null)
            {
                await Clients.Caller.SendAsync("MoveRejected", "Không tìm thấy phòng.");
                return;
            }

            var (ok, error, state) = roomManager.TryMove(room, CurrentUserId, pieceId, toRow, toCol);
            if (!ok)
            {
                await Clients.Caller.SendAsync("MoveRejected", error ?? "Nước đi không hợp lệ.");
                if (state is not null)
                    await Clients.Caller.SendAsync("GameState", state);
                return;
            }

            await Clients.Group(roomId).SendAsync("GameState", state);

            if (state is { Finished: true })
            {
                await Clients.All.SendAsync("RoomsUpdated", roomManager.GetAllRooms(BaseUrl()));
            }
        }

        // Bắt đầu ván mới (đổi phe) sau khi ván trước kết thúc
        public async Task Rematch(string roomId)
        {
            var room = roomManager.GetRoom(roomId);
            if (room is null) return;
            var (ok, error) = roomManager.Rematch(room, CurrentUserId);
            if (!ok)
            {
                await Clients.Caller.SendAsync("MoveRejected", error ?? "Không thể bắt đầu ván mới.");
                return;
            }
            await Clients.Group(roomId).SendAsync("GameState", roomManager.GetGameState(room));
            await Clients.Group(roomId).SendAsync("ReceiveMessage", "Hệ thống", "Ván mới đã bắt đầu, hai bên đổi phe.");
        }

        private string BaseUrl() =>
            $"{Context.GetHttpContext()?.Request.Scheme ?? "http"}://{Context.GetHttpContext()?.Request.Host.Value ?? "localhost:5275"}";

        private async Task BroadcastGameState(string roomId)
        {
            var room = roomManager.GetRoom(roomId);
            var state = room is null ? null : roomManager.GetGameState(room);
            if (state is not null)
                await Clients.Group(roomId).SendAsync("GameState", state);
        }
        public async Task SendMessage(
            string roomId,
            string userName,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            message = message.Trim();
            userName = CurrentDisplayName;

            await Clients
                .Group(roomId)
                .SendAsync(
                    "ReceiveMessage",
                    userName,
                    message
                );
        }

        // Người gọi gửi Offer
        public async Task SendOffer(
            string roomId,
            string offer)
        {
            await Clients
                .OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveOffer",
                    offer
                );
        }

        // Người nhận gửi Answer
        public async Task SendAnswer(
            string roomId,
            string answer)
        {
            await Clients
                .OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveAnswer",
                    answer
                );
        }

        // Trao đổi ICE Candidate
        public async Task SendIceCandidate(
            string roomId,
            string candidate)
        {
            await Clients
                .OthersInGroup(roomId)
                .SendAsync(
                    "ReceiveIceCandidate",
                    candidate
                );
        }
    }
}
