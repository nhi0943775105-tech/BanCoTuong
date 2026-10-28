(function () {
    const $ = (selector) => document.querySelector(selector);
    const $$ = (selector) => [...document.querySelectorAll(selector)];
    const roomKey = "hongky_rooms";
    let rooms = JSON.parse(localStorage.getItem(roomKey) || "null") || [
        { id: "839201", name: "Kỳ đài buổi tối", owner: "Minh Quân", mode: "public", status: "waiting", total: "10", move: "30", side: "random", spectators: true },
        { id: "482715", name: "Thử sức cuối tuần", owner: "Lan Anh", mode: "public", status: "playing", total: "15", move: "60", side: "red", spectators: true },
        { id: "706324", name: "Trận giao hữu", owner: "Đức Thành", mode: "private", status: "waiting", password: "", total: "20", move: "0", side: "black", spectators: false }
    ];
    let currentFilter = "all", currentRoom = null;
    const save = () => localStorage.setItem(roomKey, JSON.stringify(rooms));
    const session = () => window.hongKyAuth.account || {};
    const display = () => session().displayName || "Kỳ thủ";
    const escapeHtml = (value) => String(value).replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));

    function showLobby() { $("#lobby-player-name").textContent = display(); $("#lobby-shell").classList.remove("hidden"); $("#app").classList.add("hidden"); renderRooms(); }
    function formatTime(room) { return `${room.total} phút · ${room.move === "0" ? "Không giới hạn" : room.move + " giây/nước"}`; }
    function renderRooms() {
        const query = ($("#room-search").value || "").toLowerCase();
        const visible = rooms.filter((room) => (currentFilter === "all" || (currentFilter === "private" ? room.mode === "private" : room.status === "waiting")) && `${room.name} ${room.owner}`.toLowerCase().includes(query));
        $("#room-list").innerHTML = visible.map((room) => { const playing = room.status === "playing"; const action = playing ? (room.spectators ? "👁 Xem trận" : "Phòng đã đủ người") : "Vào bàn"; return `<article class="room-card"><div class="room-card-top"><span class="room-badge ${room.mode === "private" ? "private" : ""}">${room.mode === "private" ? "🔒 Riêng tư" : "🔓 Công khai"}</span><span class="room-status">${playing ? "● Đang thi đấu" : "○ Đang đợi đối thủ"}</span></div><h3 title="${escapeHtml(room.name)}">${escapeHtml(room.name)}</h3><div class="room-owner">Chủ phòng · ${escapeHtml(room.owner)}</div><div class="room-meta"><span>⏱ ${formatTime(room)}</span><span>♟ ${room.side === "random" ? "Ngẫu nhiên" : room.side === "red" ? "Đỏ" : "Đen"}</span></div><button class="primary-button room-card-action ${playing ? "watch" : ""}" data-room-id="${room.id}" ${playing && !room.spectators ? "disabled" : ""}>${action} <span>→</span></button></article>`; }).join(""); $("#empty-rooms").classList.toggle("hidden", visible.length > 0); $$("[data-room-id]").forEach((button) => button.addEventListener("click", () => accessRoom(button.dataset.roomId))); }
    function enterRoom(room, role) { currentRoom = room; $("#lobby-shell").classList.add("hidden"); $("#app").classList.remove("hidden"); localStorage.setItem("hongky_current_room", JSON.stringify({ id: room.id, role })); history.pushState({}, "", `/room/${room.id}`); }
    function accessRoom(id) { const room = rooms.find((item) => item.id === id); if (!room) return; if (room.status === "playing" && !room.spectators) return; if (room.mode === "private" && room.password) { currentRoom = room; $("#access-room-copy").textContent = `Phòng “${room.name}” yêu cầu mật khẩu để tham gia.`; $("#access-password").value = ""; $("#access-feedback").textContent = ""; $("#room-access-modal").classList.remove("hidden"); $("#access-password").focus(); } else enterRoom(room, room.status === "playing" ? "spectator" : "player2"); }
    function roomLink(room) { const base = `${location.origin}/room/${room.id}`; return room.mode === "private" && room.password ? `${base}?token=${btoa(`${room.id}:${room.password}`)}` : base; }
    function openCreate() { const account = session(); $("#room-name").value = `Phòng của ${account.displayName || "Kỳ thủ"}`; $("#create-room-feedback").textContent = ""; $("#create-room-modal").classList.remove("hidden"); }

    window.addEventListener("auth:success", showLobby);
    $("#create-room-button").addEventListener("click", openCreate); $("#room-search").addEventListener("input", renderRooms);
    $$('[data-room-filter]').forEach((button) => button.addEventListener("click", () => { currentFilter = button.dataset.roomFilter; $$('[data-room-filter]').forEach((item) => item.classList.toggle("active", item === button)); renderRooms(); }));
    $$('[data-close-room-modal]').forEach((button) => button.addEventListener("click", () => $("#create-room-modal").classList.add("hidden")));
    $$('[data-close-access-modal]').forEach((button) => button.addEventListener("click", () => $("#room-access-modal").classList.add("hidden")));
    $$('[data-close-invite-modal]').forEach((button) => button.addEventListener("click", () => $("#invite-modal").classList.add("hidden")));
    $$('input[name="room-mode"]').forEach((input) => input.addEventListener("change", () => { $$('.mode-option').forEach((option) => option.classList.toggle("active", option.querySelector("input").checked)); $("#private-options").classList.toggle("hidden", input.value !== "private" || !input.checked); }));
    $("#create-room-form").addEventListener("submit", (event) => { event.preventDefault(); const mode = $("input[name='room-mode']:checked").value, password = $("#room-password").value.trim(); if (mode === "private" && password && !/^[A-Za-z0-9]{4,6}$/.test(password)) { $("#create-room-feedback").textContent = "Mật khẩu phải gồm 4–6 chữ số hoặc ký tự, không dấu cách."; return; } const room = { id: String(Math.floor(100000 + Math.random() * 899999)), name: $("#room-name").value.trim() || `Phòng của ${display()}`, owner: display(), mode, password, status: "waiting", total: $("#room-total-time").value, move: $("#room-move-time").value, side: $("#room-side").value, spectators: $("#room-spectators").checked }; rooms = [room, ...rooms]; save(); $("#create-room-modal").classList.add("hidden"); renderRooms(); currentRoom = room; $("#invite-link-input").value = roomLink(room); $("#invite-feedback").textContent = ""; $("#invite-modal").classList.remove("hidden"); });
    $("#room-access-form").addEventListener("submit", (event) => { event.preventDefault(); if ($("#access-password").value !== currentRoom.password) { $("#access-feedback").textContent = "Mật khẩu chưa chính xác, vui lòng thử lại"; return; } $("#room-access-modal").classList.add("hidden"); enterRoom(currentRoom, currentRoom.status === "playing" ? "spectator" : "player2"); });
    $("#copy-invite").addEventListener("click", async () => { try { await navigator.clipboard.writeText($("#invite-link-input").value); $("#invite-feedback").textContent = "Đã sao chép link phòng."; } catch { $("#invite-link-input").select(); document.execCommand("copy"); $("#invite-feedback").textContent = "Đã sao chép link phòng."; } });
    $("#logout-button").addEventListener("click", async () => {
        const button = $("#logout-button");
        button.disabled = true;
        try { await window.hongKyAuth.post("logout"); window.hongKyAuth.account = null; location.assign("/"); }
        catch (error) { alert(error.message); button.disabled = false; }
    });
    window.hongKyAuth.ready.then((account) => {
        if (!account) return;
        showLobby();
        const match = location.pathname.match(/\/room\/(\d+)/);
        if (!match) return;
        const linkedRoom = rooms.find((room) => room.id === match[1]);
        if (!linkedRoom) return;
        const token = new URLSearchParams(location.search).get("token");
        if (linkedRoom.mode === "private" && linkedRoom.password && token !== btoa(`${linkedRoom.id}:${linkedRoom.password}`)) accessRoom(linkedRoom.id);
        else if (linkedRoom.status === "playing" && !linkedRoom.spectators) alert("Phòng đã đủ người chơi");
        else enterRoom(linkedRoom, linkedRoom.status === "playing" ? "spectator" : "player2");
    });
})();
