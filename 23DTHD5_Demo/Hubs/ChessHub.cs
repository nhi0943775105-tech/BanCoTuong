using System.IO.Pipelines;
using _23DTHD5_Demo.Models;
using Microsoft.AspNetCore.SignalR;

namespace _23DTHD5_Demo.Hubs
{
    public class ChessHub: Hub
    {
        // =========================================
        // Người chơi tham gia room
        // =========================================
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                roomId
            );

            Console.WriteLine(
                $"{Context.ConnectionId} joined room {roomId}"
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

            Console.WriteLine(
                $"{Context.ConnectionId} left room {roomId}"
            );
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

