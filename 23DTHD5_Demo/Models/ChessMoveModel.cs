namespace _23DTHD5_Demo.Models
{
    public class ChessMoveModel
    {
        public string PieceId { get; set; } = "";

        public int FromRow { get; set; }
        public int FromCol { get; set; }

        public int ToRow { get; set; }
        public int ToCol { get; set; }
    }
}
