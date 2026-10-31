using System.Text.Json;
using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;

// Kiểm tra luật cờ tướng và quản lý ván cờ trong phòng. Không cần database hay mạng.
internal static class XiangqiRulesChecks
{
    private static int checks;
    private const string R = XiangqiGame.Red;
    private const string B = XiangqiGame.Black;

    public static int Main()
    {
        InitialPosition();
        InitialPositionMatchesJson();
        General();
        Advisor();
        Elephant();
        Horse();
        Rook();
        Cannon();
        Pawn();
        TurnsAndOwnership();
        PinnedPieceAndFlyingGeneral();
        CheckAndCheckmate();
        StalemateIsLoss();
        RoomGameFlow();
        Console.WriteLine($"OK: {checks} kiểm tra đạt.");
        return 0;
    }

    private static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) throw new InvalidOperationException("FAILED: " + name);
    }

    private static XqPiece P(string id, string type, string color, int row, int col) =>
        new() { Id = id, Type = type, Color = color, File = id + ".svg", Row = row, Col = col };

    // Hai Tướng ở hai cột khác nhau để không bị đối mặt, trừ khi test yêu cầu.
    private static List<XqPiece> Generals(int redCol = 4, int blackCol = 3) =>
    [
        P("chu_tuong_do", "chu_tuong", R, 9, redCol),
        P("chu_tuong_den", "chu_tuong", B, 0, blackCol)
    ];

    private static XiangqiGame Game(string turn, params XqPiece[] extra) =>
        XiangqiGame.FromPieces(Generals().Concat(extra), turn);

    private static bool Legal(XiangqiGame g, string id, int row, int col)
    {
        var piece = g.GetPiece(id)!;
        return g.LegalMoves(piece).Contains((row, col));
    }

    private static void InitialPosition()
    {
        var g = new XiangqiGame();
        var pieces = g.Pieces;
        Check(XiangqiGame.Rows == 10 && XiangqiGame.Cols == 9, "Bàn cờ 10 hàng x 9 cột");
        Check(pieces.Count == 32, "Có 32 quân");
        Check(pieces.Select(p => p.Id).Distinct().Count() == 32, "Id quân không trùng");
        Check(pieces.All(p => p.Row is >= 0 and <= 9 && p.Col is >= 0 and <= 8), "Mọi quân nằm trong bàn cờ");
        Check(pieces.Select(p => (p.Row, p.Col)).Distinct().Count() == 32, "Không có hai quân cùng ô");
        Check(g.Turn == R, "Đỏ đi trước");
        foreach (var color in new[] { R, B })
        {
            var mine = pieces.Where(p => p.Color == color).ToList();
            Check(mine.Count == 16, $"{color} có 16 quân");
            Check(mine.Count(p => p.Type == "xe") == 2, $"{color} có 2 Xe");
            Check(mine.Count(p => p.Type == "ma") == 2, $"{color} có 2 Mã");
            Check(mine.Count(p => p.Type == "tuong") == 2, $"{color} có 2 Tượng");
            Check(mine.Count(p => p.Type == "si") == 2, $"{color} có 2 Sĩ");
            Check(mine.Count(p => p.Type == "phao") == 2, $"{color} có 2 Pháo");
            Check(mine.Count(p => p.Type == "tot") == 5, $"{color} có 5 Tốt");
            Check(mine.Count(p => p.Type == "chu_tuong") == 1, $"{color} có 1 Tướng");
        }
        Check(g.PieceAt(9, 4)?.Id == "chu_tuong_do" && g.PieceAt(0, 4)?.Id == "chu_tuong_den", "Tướng ở giữa cung");
        Check(g.PieceAt(7, 1)?.Type == "phao" && g.PieceAt(2, 7)?.Type == "phao", "Pháo ở hàng 7 và hàng 2");
        Check(!g.InCheck, "Thế cờ ban đầu không bị chiếu");
    }

    private static void InitialPositionMatchesJson()
    {
        var dir = AppContext.BaseDirectory;
        string? json = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "23DTHD5_Demo", "wwwroot", "co_tuong_initial.json");
            if (File.Exists(candidate)) { json = File.ReadAllText(candidate); break; }
        }
        Check(json is not null, "Tìm thấy co_tuong_initial.json");
        using var doc = JsonDocument.Parse(json!);
        var expected = doc.RootElement.GetProperty("pieces").EnumerateArray().Select(e => (
            Id: e.GetProperty("id").GetString()!,
            Type: e.GetProperty("type").GetString()!,
            Color: e.GetProperty("color").GetString()!,
            File: e.GetProperty("file").GetString()!,
            Row: e.GetProperty("row").GetInt32(),
            Col: e.GetProperty("col").GetInt32())).OrderBy(x => x.Id).ToList();
        var actual = new XiangqiGame().Pieces.Select(p => (p.Id, p.Type, p.Color, p.File, p.Row, p.Col))
            .OrderBy(x => x.Id).ToList();
        Check(expected.SequenceEqual(actual), "Thế cờ server khớp co_tuong_initial.json");
    }

    private static void General()
    {
        var g = Game(R);
        Check(Legal(g, "chu_tuong_do", 8, 4), "Tướng đi lên 1 ô");
        Check(Legal(g, "chu_tuong_do", 9, 5), "Tướng đi ngang 1 ô");
        Check(!Legal(g, "chu_tuong_do", 8, 3), "Tướng không đi chéo");
        Check(!Legal(g, "chu_tuong_do", 7, 4), "Tướng không đi 2 ô");
        var edge = XiangqiGame.FromPieces([P("chu_tuong_do", "chu_tuong", R, 7, 3), P("chu_tuong_den", "chu_tuong", B, 0, 4)], R);
        Check(!Legal(edge, "chu_tuong_do", 6, 3), "Tướng không ra khỏi cung (hàng)");
        Check(!Legal(edge, "chu_tuong_do", 7, 2), "Tướng không ra khỏi cung (cột)");
    }

    private static void Advisor()
    {
        var g = Game(R, P("si_do_1", "si", R, 9, 3));
        Check(Legal(g, "si_do_1", 8, 4), "Sĩ đi chéo 1 ô trong cung");
        Check(!Legal(g, "si_do_1", 8, 3), "Sĩ không đi thẳng");
        Check(!Legal(g, "si_do_1", 8, 2), "Sĩ không ra ngoài cung");
    }

    private static void Elephant()
    {
        var g = Game(R, P("tuong_do_1", "tuong", R, 9, 2));
        Check(Legal(g, "tuong_do_1", 7, 0) && Legal(g, "tuong_do_1", 7, 4), "Tượng đi chéo 2 ô");
        var blocked = Game(R, P("tuong_do_1", "tuong", R, 9, 2), P("tot_den_x", "tot", B, 8, 3));
        Check(!Legal(blocked, "tuong_do_1", 7, 4), "Tượng bị cản mắt");
        var river = Game(R, P("tuong_do_1", "tuong", R, 5, 2));
        Check(!Legal(river, "tuong_do_1", 3, 0) && !Legal(river, "tuong_do_1", 3, 4), "Tượng không qua sông");
        Check(Legal(river, "tuong_do_1", 7, 0) && Legal(river, "tuong_do_1", 7, 4), "Tượng lùi về phía mình");
    }

    private static void Horse()
    {
        var g = Game(R, P("ma_do_1", "ma", R, 5, 4));
        var expected = new[] { (3, 3), (3, 5), (4, 2), (4, 6), (6, 2), (6, 6), (7, 3), (7, 5) };
        Check(expected.All(t => Legal(g, "ma_do_1", t.Item1, t.Item2)), "Mã đi đủ 8 hướng chữ L");
        var legBlocked = Game(R, P("ma_do_1", "ma", R, 5, 4), P("tot_do_x", "tot", R, 4, 4));
        Check(!Legal(legBlocked, "ma_do_1", 3, 3) && !Legal(legBlocked, "ma_do_1", 3, 5), "Mã bị cản chân (phía trước)");
        Check(Legal(legBlocked, "ma_do_1", 4, 2), "Mã không bị cản ở hướng khác");
        var sideBlocked = Game(R, P("ma_do_1", "ma", R, 5, 4), P("tot_do_x", "tot", R, 5, 5));
        Check(!Legal(sideBlocked, "ma_do_1", 4, 6) && !Legal(sideBlocked, "ma_do_1", 6, 6), "Mã bị cản chân (bên cạnh)");
    }

    private static void Rook()
    {
        var g = Game(R, P("xe_do_1", "xe", R, 5, 4), P("tot_den_x", "tot", B, 5, 7), P("tot_do_x", "tot", R, 3, 4));
        Check(Legal(g, "xe_do_1", 5, 0) && Legal(g, "xe_do_1", 5, 6), "Xe đi ngang tới chỗ trống");
        Check(Legal(g, "xe_do_1", 5, 7), "Xe ăn quân địch cuối đường");
        Check(!Legal(g, "xe_do_1", 5, 8), "Xe không nhảy qua quân");
        Check(!Legal(g, "xe_do_1", 3, 4) && Legal(g, "xe_do_1", 4, 4), "Xe không ăn quân mình, không vượt qua");
        Check(!Legal(g, "xe_do_1", 4, 5), "Xe không đi chéo");
    }

    private static void Cannon()
    {
        var g = Game(R,
            P("phao_do_1", "phao", R, 5, 1),
            P("tot_do_x", "tot", R, 5, 4),
            P("tot_den_x", "tot", B, 5, 7),
            P("tot_den_y", "tot", B, 3, 1));
        Check(Legal(g, "phao_do_1", 5, 3) && !Legal(g, "phao_do_1", 5, 5), "Pháo đi như Xe khi không ăn");
        Check(Legal(g, "phao_do_1", 4, 1), "Pháo đi dọc");
        Check(Legal(g, "phao_do_1", 5, 7), "Pháo ăn quân qua đúng 1 ngòi");
        Check(!Legal(g, "phao_do_1", 5, 4), "Pháo không ăn quân mình");

        var direct = Game(R, P("phao_do_1", "phao", R, 5, 1), P("tot_den_y", "tot", B, 3, 1));
        Check(!Legal(direct, "phao_do_1", 3, 1), "Pháo không ăn trực tiếp khi không có ngòi");
        var two = Game(R, P("phao_do_1", "phao", R, 5, 1), P("a", "tot", R, 5, 3), P("b", "tot", R, 5, 5), P("c", "tot", B, 5, 7));
        Check(!Legal(two, "phao_do_1", 5, 7), "Pháo không ăn khi có 2 ngòi");
    }

    private static void Pawn()
    {
        var g = Game(R, P("tot_do_1", "tot", R, 6, 4));
        Check(Legal(g, "tot_do_1", 5, 4), "Tốt đỏ tiến lên");
        Check(!Legal(g, "tot_do_1", 6, 3) && !Legal(g, "tot_do_1", 7, 4), "Tốt chưa qua sông không đi ngang/lùi");
        var crossed = Game(R, P("tot_do_1", "tot", R, 4, 4));
        Check(Legal(crossed, "tot_do_1", 3, 4) && Legal(crossed, "tot_do_1", 4, 3) && Legal(crossed, "tot_do_1", 4, 5), "Tốt qua sông đi tiến và ngang");
        Check(!Legal(crossed, "tot_do_1", 5, 4), "Tốt không đi lùi");
        var black = Game(B, P("tot_den_1", "tot", B, 3, 4));
        Check(Legal(black, "tot_den_1", 4, 4) && !Legal(black, "tot_den_1", 3, 3), "Tốt đen tiến xuống");
        var blackCrossed = Game(B, P("tot_den_1", "tot", B, 5, 4));
        Check(Legal(blackCrossed, "tot_den_1", 6, 4) && Legal(blackCrossed, "tot_den_1", 5, 3) && !Legal(blackCrossed, "tot_den_1", 4, 4), "Tốt đen qua sông");
    }

    private static void TurnsAndOwnership()
    {
        var g = new XiangqiGame();
        Check(!g.TryMove(B, "xe_den_1", 1, 0).Ok, "Đen không được đi trước");
        Check(!g.TryMove(R, "xe_den_1", 1, 0).Ok, "Không điều khiển quân đối phương");
        Check(!g.TryMove("", "xe_do_1", 8, 0).Ok, "Người xem không được đi");
        Check(!g.TryMove(R, "xe_do_1", 5, 0).Ok, "Xe đỏ không nhảy qua Tốt");
        Check(!g.TryMove(R, "khong_ton_tai", 5, 0).Ok, "Quân không tồn tại");
        Check(!g.TryMove(R, "xe_do_1", 10, 0).Ok, "Ra ngoài bàn cờ");
        Check(g.TryMove(R, "phao_do_2", 7, 4).Ok, "Pháo đầu (7,7)->(7,4) hợp lệ");
        Check(g.Turn == B, "Đổi lượt sau nước đi hợp lệ");
        Check(!g.TryMove(R, "xe_do_1", 8, 0).Ok, "Đỏ không đi hai lần liên tiếp");
        Check(g.TryMove(B, "ma_den_2", 2, 6).Ok, "Đen đi Mã hợp lệ");
        var capture = g.TryMove(R, "phao_do_2", 3, 4);
        Check(capture.Ok && capture.Captured?.Id == "tot_den_3", "Pháo ăn Tốt qua đúng 1 ngòi");
        Check(g.GetPiece("tot_den_3") is null && g.Pieces.Count == 31, "Quân bị ăn biến mất khỏi bàn cờ");
    }

    private static void PinnedPieceAndFlyingGeneral()
    {
        // Tướng hai bên cùng cột 4, chỉ có Xe đỏ ở giữa: Xe không được rời cột.
        var g = XiangqiGame.FromPieces(
        [
            P("chu_tuong_do", "chu_tuong", R, 9, 4),
            P("chu_tuong_den", "chu_tuong", B, 0, 4),
            P("xe_do_1", "xe", R, 5, 4)
        ], R);
        Check(!Legal(g, "xe_do_1", 5, 0), "Quân bị ghim không được rời cột (hai Tướng đối mặt)");
        Check(Legal(g, "xe_do_1", 1, 4) || Legal(g, "xe_do_1", 4, 4), "Xe vẫn đi dọc cột được");
        var faceOff = XiangqiGame.FromPieces([P("chu_tuong_do", "chu_tuong", R, 9, 4), P("chu_tuong_den", "chu_tuong", B, 0, 3)], R);
        Check(!Legal(faceOff, "chu_tuong_do", 9, 3), "Tướng không đi vào cột đối mặt Tướng địch (cột 3)");
        Check(Legal(faceOff, "chu_tuong_do", 9, 5), "Tướng đi sang cột khác hợp lệ");
        var both = XiangqiGame.FromPieces([P("chu_tuong_do", "chu_tuong", R, 9, 3), P("chu_tuong_den", "chu_tuong", B, 0, 4)], R);
        Check(!Legal(both, "chu_tuong_do", 9, 4), "Tướng không được đi vào cột đối mặt Tướng địch");
    }

    private static void CheckAndCheckmate()
    {
        // Xe đỏ chiếu Tướng đen trên cột 3.
        var g = XiangqiGame.FromPieces(
        [
            P("chu_tuong_do", "chu_tuong", R, 9, 5),
            P("chu_tuong_den", "chu_tuong", B, 0, 4),
            P("xe_do_1", "xe", R, 6, 0)
        ], R);
        var r = g.TryMove(R, "xe_do_1", 6, 4);
        Check(r.Ok && g.InCheck && !g.IsFinished, "Chiếu Tướng nhưng còn đường thoát");
        Check(!g.TryMove(B, "chu_tuong_den", 1, 4).Ok, "Tướng bị chiếu không được đứng yên trong đường chiếu");
        Check(g.TryMove(B, "chu_tuong_den", 0, 3).Ok, "Tướng thoát chiếu sang ô an toàn");

        // Chiếu hết: hai Xe đỏ khoá Tướng đen ở cung.
        var mate = XiangqiGame.FromPieces(
        [
            P("chu_tuong_do", "chu_tuong", R, 9, 3),
            P("chu_tuong_den", "chu_tuong", B, 0, 4),
            P("xe_do_1", "xe", R, 1, 0),
            P("xe_do_2", "xe", R, 5, 8)
        ], R);
        var m = mate.TryMove(R, "xe_do_2", 0, 8);
        Check(m.Ok, "Nước chiếu hết hợp lệ");
        Check(mate.IsFinished && mate.Winner == R && mate.EndReason == "checkmate", "Phát hiện chiếu hết, Đỏ thắng");
        Check(!mate.TryMove(B, "chu_tuong_den", 0, 3).Ok, "Không đi tiếp sau khi ván kết thúc");
    }

    private static void StalemateIsLoss()
    {
        // Đen không bị chiếu nhưng không còn nước đi hợp lệ: Đen thua.
        var stale = XiangqiGame.FromPieces(
        [
            P("chu_tuong_do", "chu_tuong", R, 9, 5),
            P("chu_tuong_den", "chu_tuong", B, 0, 3),
            P("xe_do_1", "xe", R, 5, 4),
            P("xe_do_2", "xe", R, 6, 2),
            P("xe_do_3", "xe", R, 3, 0)
        ], R);
        var m = stale.TryMove(R, "xe_do_3", 1, 0);
        Check(m.Ok, "Nước đi dẫn tới hết nước đi");
        Check(stale.IsFinished && stale.Winner == R, "Hết nước đi hợp lệ thì thua");
    }

    private static void RoomGameFlow()
    {
        var rooms = new RoomManagerService();
        var (dto, _) = rooms.CreateRoom("u1", "Người 1", new CreateRoomRequest { Name = "Test", Side = "red" }, "http://localhost");
        var room = rooms.GetRoom(dto.Id)!;
        Check(!rooms.EnsureGame(room), "Chưa đủ người thì chưa có ván");
        var join = rooms.TryJoinRoom("u2", "Người 2", new JoinRoomRequest { RoomId = room.Id }, "http://localhost");
        Check(join.Succeeded && join.Role == "player2", "Người thứ hai vào phòng");
        var spectator = rooms.TryJoinRoom("u3", "Khán giả", new JoinRoomRequest { RoomId = room.Id }, "http://localhost");
        Check(spectator.Succeeded, "Khán giả vào phòng");
        Check(rooms.EnsureGame(room) && room.Status == "playing", "Đủ 2 người thì bắt đầu ván");
        Check(!rooms.EnsureGame(room), "Không tạo lại ván khi đang chơi");
        Check(room.RedId == "u1" && room.BlackId == "u2", "Chủ phòng chọn Đỏ thì được Đỏ");

        Check(!rooms.TryMove(room, "u2", "xe_den_1", 1, 0).Ok, "Đen đi trước bị từ chối");

        Check(!rooms.TryMove(room, "u3", "phao_do_2", 7, 4).Ok, "Khán giả không được đi");
        Check(!rooms.TryMove(room, "u1", "xe_den_1", 1, 0).Ok, "Đỏ không điều khiển quân Đen");
        var ok = rooms.TryMove(room, "u1", "phao_do_2", 7, 4);
        Check(ok.Ok && ok.State!.Turn == B && ok.State.MoveNumber == 1, "Nước đi hợp lệ cập nhật trạng thái");
        Check(rooms.GetLegalMoves(room, "u1", "xe_do_1").Count == 0, "Không có nước gợi ý khi chưa đến lượt");
        Check(rooms.GetLegalMoves(room, "u2", "ma_den_2").Count > 0, "Có nước gợi ý cho quân đến lượt");
        Check(rooms.GetGameState(room)!.Pieces.Count == 32, "Cả phòng thấy cùng 32 quân");

        rooms.LeaveRoom("u2", room.Id);
        var state = rooms.GetGameState(room)!;
        Check(state.Finished && state.Winner == R && state.Reason == "opponent_left", "Rời phòng giữa ván thì đối thủ thắng");
        Check(!rooms.TryMove(room, "u1", "phao_do_1", 7, 4).Ok, "Không đi được sau khi ván kết thúc");
        rooms.TryJoinRoom("u4", "Người 4", new JoinRoomRequest { RoomId = room.Id }, "http://localhost");
        Check(rooms.EnsureGame(room) && !rooms.GetGameState(room)!.Finished, "Người mới vào thì có ván mới");
    }
}
