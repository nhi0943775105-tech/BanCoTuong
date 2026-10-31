namespace _23DTHD5_Demo.Services;

/// <summary>Một quân cờ. Color: "do" (Đỏ, đi trước, nằm dưới) hoặc "den" (Đen, nằm trên).</summary>
public class XqPiece
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // xe, ma, tuong, si, chu_tuong, phao, tot
    public string Color { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Col { get; set; }

    public XqPiece Clone() => (XqPiece)MemberwiseClone();
}

public record XqMoveResult(bool Ok, string? Error = null, XqPiece? Captured = null);

public record XqLastMove(string PieceId, int FromRow, int FromCol, int ToRow, int ToCol);

public class GameStateDto
{
    public string RoomId { get; set; } = string.Empty;
    public List<XqPiece> Pieces { get; set; } = [];
    public string Turn { get; set; } = XiangqiGame.Red;
    public bool InCheck { get; set; }
    public bool Finished { get; set; }
    public string? Winner { get; set; }
    public string? Reason { get; set; }
    public int MoveNumber { get; set; }
    public XqLastMove? LastMove { get; set; }
    public string? RedId { get; set; }
    public string? BlackId { get; set; }
    public string RedName { get; set; } = string.Empty;
    public string BlackName { get; set; } = string.Empty;
}

/// <summary>
/// Luật cờ tướng chạy ở server (nguồn sự thật duy nhất của ván cờ).
/// Bàn cờ 10 hàng (0..9) x 9 cột (0..8). Đen ở hàng 0-4, Đỏ ở hàng 5-9.
/// </summary>
public class XiangqiGame
{
    public const int Rows = 10;
    public const int Cols = 9;
    public const string Red = "do";
    public const string Black = "den";

    private readonly XqPiece?[,] _grid = new XqPiece?[Rows, Cols];

    public string Turn { get; private set; } = Red;
    public bool InCheck { get; private set; }
    public bool IsFinished { get; private set; }
    public string? Winner { get; private set; }
    public string? EndReason { get; private set; }
    public int MoveNumber { get; private set; }
    public XqLastMove? LastMove { get; private set; }

    public XiangqiGame() => SetupInitialPosition();

    /// <summary>Tạo thế cờ tuỳ ý (dùng cho kiểm thử).</summary>
    public static XiangqiGame FromPieces(IEnumerable<XqPiece> pieces, string turn = Red)
    {
        var game = new XiangqiGame(empty: true) { Turn = turn };
        foreach (var piece in pieces)
        {
            game._grid[piece.Row, piece.Col] = piece.Clone();
        }
        game.InCheck = game.IsInCheck(game.Turn);
        return game;
    }

    private XiangqiGame(bool empty)
    {
    }

    public static string Opponent(string color) => color == Red ? Black : Red;

    public IReadOnlyList<XqPiece> Pieces
    {
        get
        {
            var list = new List<XqPiece>(32);
            for (var r = 0; r < Rows; r++)
                for (var c = 0; c < Cols; c++)
                    if (_grid[r, c] is { } p) list.Add(p.Clone());
            return list;
        }
    }

    public XqPiece? GetPiece(string pieceId)
    {
        foreach (var p in AllPieces())
            if (p.Id == pieceId) return p;
        return null;
    }

    public XqPiece? PieceAt(int row, int col) => InBoard(row, col) ? _grid[row, col] : null;

    private IEnumerable<XqPiece> AllPieces()
    {
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                if (_grid[r, c] is { } p) yield return p;
    }

    // ------------------------------------------------------------------
    // Khởi tạo
    // ------------------------------------------------------------------
    private void SetupInitialPosition()
    {
        // Thứ tự hàng sau: Xe, Mã, Tượng, Sĩ, Tướng, Sĩ, Tượng, Mã, Xe (khớp co_tuong_initial.json)
        string[] back = ["xe", "ma", "tuong", "si", "chu_tuong", "si", "tuong", "ma", "xe"];
        foreach (var (color, suffix, backRow, cannonRow, pawnRow) in new[]
                 {
                     (Black, "den", 0, 2, 3),
                     (Red, "do", 9, 7, 6)
                 })
        {
            var counters = new Dictionary<string, int>();
            for (var c = 0; c < Cols; c++)
            {
                var type = back[c];
                counters[type] = counters.GetValueOrDefault(type) + 1;
                Add(color, suffix, type, counters[type], backRow, c);
            }
            Add(color, suffix, "phao", 1, cannonRow, 1);
            Add(color, suffix, "phao", 2, cannonRow, 7);
            for (var i = 0; i < 5; i++)
                Add(color, suffix, "tot", i + 1, pawnRow, i * 2);
        }
    }

    private void Add(string color, string suffix, string type, int index, int row, int col)
    {
        var generalType = type == "chu_tuong";
        var id = generalType ? $"chu_tuong_{suffix}" : $"{type}_{suffix}_{index}";
        var file = generalType ? $"chutuong{suffix}.svg" : $"{type}{suffix}{index}.svg";
        _grid[row, col] = new XqPiece { Id = id, Type = type, Color = color, File = file, Row = row, Col = col };
    }

    // ------------------------------------------------------------------
    // Thực hiện nước đi
    // ------------------------------------------------------------------
    public XqMoveResult TryMove(string color, string pieceId, int toRow, int toCol)
    {
        if (IsFinished) return new XqMoveResult(false, "Ván cờ đã kết thúc.");
        if (color != Red && color != Black) return new XqMoveResult(false, "Bạn không phải người chơi trong ván này.");
        if (color != Turn) return new XqMoveResult(false, "Chưa đến lượt của bạn.");

        var piece = GetPiece(pieceId);
        if (piece is null) return new XqMoveResult(false, "Không tìm thấy quân cờ.");
        if (piece.Color != color) return new XqMoveResult(false, "Đây không phải quân của bạn.");
        if (!InBoard(toRow, toCol)) return new XqMoveResult(false, "Nước đi nằm ngoài bàn cờ.");
        if (!CanReach(piece, toRow, toCol)) return new XqMoveResult(false, "Nước đi không đúng luật.");
        if (LeavesGeneralInCheck(piece, toRow, toCol))
            return new XqMoveResult(false, "Nước đi này làm Tướng của bạn bị chiếu hoặc hai Tướng đối mặt.");

        var captured = _grid[toRow, toCol]?.Clone();
        var fromRow = piece.Row;
        var fromCol = piece.Col;
        _grid[fromRow, fromCol] = null;
        _grid[toRow, toCol] = piece;
        piece.Row = toRow;
        piece.Col = toCol;

        MoveNumber++;
        LastMove = new XqLastMove(piece.Id, fromRow, fromCol, toRow, toCol);
        Turn = Opponent(color);
        InCheck = IsInCheck(Turn);

        if (!HasAnyLegalMove(Turn))
        {
            IsFinished = true;
            Winner = color;
            // Cờ tướng: hết nước đi hợp lệ (kể cả không bị chiếu) đều thua.
            EndReason = InCheck ? "checkmate" : "stalemate";
        }

        return new XqMoveResult(true, null, captured);
    }

    public void EndGame(string? winner, string reason)
    {
        if (IsFinished) return;
        IsFinished = true;
        Winner = winner;
        EndReason = reason;
    }

    // ------------------------------------------------------------------
    // Nước đi hợp lệ
    // ------------------------------------------------------------------
    public List<(int Row, int Col)> LegalMoves(XqPiece piece)
    {
        var result = new List<(int, int)>();
        if (IsFinished) return result;
        var actual = GetPiece(piece.Id);
        if (actual is null) return result;
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                if (CanReach(actual, r, c) && !LeavesGeneralInCheck(actual, r, c))
                    result.Add((r, c));
        return result;
    }

    public bool HasAnyLegalMove(string color)
    {
        foreach (var p in AllPieces().Where(p => p.Color == color).ToList())
            if (LegalMoves(p).Count > 0) return true;
        return false;
    }

    public bool IsInCheck(string color)
    {
        XqPiece? general = null;
        foreach (var p in AllPieces())
            if (p.Type == "chu_tuong" && p.Color == color) { general = p; break; }
        if (general is null) return true;

        foreach (var e in AllPieces())
        {
            if (e.Color == color) continue;
            if (e.Type == "chu_tuong")
            {
                // Hai Tướng đối mặt (không có quân giữa) cũng được coi là chiếu.
                if (e.Col == general.Col && CountBetween(e.Row, e.Col, general.Row, general.Col) == 0)
                    return true;
                continue;
            }
            if (CanReach(e, general.Row, general.Col)) return true;
        }
        return false;
    }

    private bool LeavesGeneralInCheck(XqPiece piece, int toRow, int toCol)
    {
        var captured = _grid[toRow, toCol];
        var fromRow = piece.Row;
        var fromCol = piece.Col;
        _grid[fromRow, fromCol] = null;
        _grid[toRow, toCol] = piece;
        piece.Row = toRow;
        piece.Col = toCol;

        var inCheck = IsInCheck(piece.Color);

        _grid[toRow, toCol] = captured;
        _grid[fromRow, fromCol] = piece;
        piece.Row = fromRow;
        piece.Col = fromCol;
        return inCheck;
    }

    /// <summary>Nước đi đúng quy tắc hình học của quân (chưa xét chiếu Tướng).</summary>
    public bool CanReach(XqPiece p, int tr, int tc)
    {
        if (!InBoard(tr, tc)) return false;
        var dr = tr - p.Row;
        var dc = tc - p.Col;
        if (dr == 0 && dc == 0) return false;
        var target = _grid[tr, tc];
        if (target is not null && target.Color == p.Color) return false;
        var adr = Math.Abs(dr);
        var adc = Math.Abs(dc);

        switch (p.Type)
        {
            case "chu_tuong":
                return adr + adc == 1 && InPalace(p.Color, tr, tc);

            case "si":
                return adr == 1 && adc == 1 && InPalace(p.Color, tr, tc);

            case "tuong":
                if (adr != 2 || adc != 2) return false;
                if (!OwnSide(p.Color, tr)) return false; // Tượng không qua sông
                return _grid[p.Row + dr / 2, p.Col + dc / 2] is null; // cản mắt tượng

            case "ma":
                if (!((adr == 2 && adc == 1) || (adr == 1 && adc == 2))) return false;
                // Cản chân mã
                var legRow = adr == 2 ? p.Row + dr / 2 : p.Row;
                var legCol = adc == 2 ? p.Col + dc / 2 : p.Col;
                return _grid[legRow, legCol] is null;

            case "xe":
                if (dr != 0 && dc != 0) return false;
                return CountBetween(p.Row, p.Col, tr, tc) == 0;

            case "phao":
                if (dr != 0 && dc != 0) return false;
                var between = CountBetween(p.Row, p.Col, tr, tc);
                return target is null ? between == 0 : between == 1;

            case "tot":
                var forward = p.Color == Black ? 1 : -1;
                if (dc == 0 && dr == forward) return true;
                var crossedRiver = !OwnSide(p.Color, p.Row);
                return crossedRiver && dr == 0 && adc == 1;

            default:
                return false;
        }
    }

    // ------------------------------------------------------------------
    // Tiện ích
    // ------------------------------------------------------------------
    private static bool InBoard(int row, int col) => row is >= 0 and < Rows && col is >= 0 and < Cols;

    private static bool InPalace(string color, int row, int col) =>
        col is >= 3 and <= 5 && (color == Black ? row is >= 0 and <= 2 : row is >= 7 and <= 9);

    private static bool OwnSide(string color, int row) => color == Black ? row <= 4 : row >= 5;

    /// <summary>Đếm số quân nằm giữa hai ô trên cùng hàng hoặc cùng cột (không tính hai đầu).</summary>
    private int CountBetween(int r1, int c1, int r2, int c2)
    {
        var count = 0;
        if (r1 == r2)
        {
            for (var c = Math.Min(c1, c2) + 1; c < Math.Max(c1, c2); c++)
                if (_grid[r1, c] is not null) count++;
        }
        else if (c1 == c2)
        {
            for (var r = Math.Min(r1, r2) + 1; r < Math.Max(r1, r2); r++)
                if (_grid[r, c1] is not null) count++;
        }
        return count;
    }
}
