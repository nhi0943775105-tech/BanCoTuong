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
        public async Task MovePiece(
            string roomId,
            string pieceId,
            int fromRow,
            int fromCol,
            int toRow,
            int toCol)
        {
            var move = new ChessMoveModel
            {
                PieceId = pieceId,

                FromRow = fromRow,
                FromCol = fromCol,

                ToRow = toRow,
                ToCol = toCol
            };

            Console.WriteLine(
                $"Room: {roomId} | " +
                $"{pieceId}: " +
                $"({fromRow},{fromCol}) -> " +
                $"({toRow},{toCol})"
            );

            // Gửi nước đi cho tất cả người trong room
            await Clients
                .Group(roomId)
                .SendAsync(
                    "ReceiveMove",
                    move
                );
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
