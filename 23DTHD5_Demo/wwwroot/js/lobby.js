(function () {
    const $ = (selector) => document.querySelector(selector);
    const $$ = (selector) => [...document.querySelectorAll(selector)];
    const roomKey = "hongky_rooms";

    let rooms = [];
    let currentFilter = "all";
    let currentRoom = null;
    let currentInviteToken = null;
    let pendingIncomingInvite = null;
    let searchRefreshTimer = null;

    // Bắt URL /room/{id} hoặc ?deeplink=... ngay khi tải trang (trước khi history thay đổi)
    function detectInitialUrlTarget() {
        const params = new URLSearchParams(location.search);
        const deeplinkParam = params.get("deeplink");
        const tokenParam = params.get("token");
        const pathMatch = location.pathname.match(/\/room\/(\d{6})/);

        if (deeplinkParam) {
            const parsed = parseRoomInput(deeplinkParam);
            if (parsed.roomId) {
                return {
                    roomId: parsed.roomId,
                    token: tokenParam || parsed.token || null
                };
            }
        }
        if (pathMatch) {
            return {
                roomId: pathMatch[1],
                token: tokenParam || null
            };
        }
        return null;
    }

    const detectedTarget = detectInitialUrlTarget();
    if (detectedTarget) {
        sessionStorage.setItem("hongky_pending_deeplink", JSON.stringify(detectedTarget));
    }

    const session = () => window.hongKyAuth.account || {};
    const display = () => session().displayName || "Kỳ thủ";
    const escapeHtml = (value) =>
        String(value ?? "").replace(/[&<>"']/g, (char) => ({
            "&": "&amp;",
            "<": "&lt;",
            ">": "&gt;",
            '"': "&quot;",
            "'": "&#39;"
        }[char]));

    async function apiGet(url) {
        const response = await fetch(url, { credentials: "same-origin", cache: "no-store" });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            throw new Error(data.message || "Không thể tải dữ liệu từ máy chủ.");
        }
        return data;
    }

    async function apiPost(url, body = {}) {
        const csrfRes = await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" });
        const csrf = await csrfRes.json().catch(() => ({}));
        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": csrf.token || ""
            },
            body: JSON.stringify(body)
        });
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            const error = new Error(data.message || "Không thể xử lý yêu cầu.");
            error.code = data.code;
            error.status = response.status;
            throw error;
        }
        return data;
    }

    function parseRoomInput(rawInput) {
        const text = String(rawInput || "").trim();
        if (!text) return { roomId: "", token: null };

        // 1. Hỗ trợ URL hoặc Deep Link: cotuong://room/839201?token=... hoặc http.../room/839201?deeplink=...&token=...
        const deepMatch = text.match(/(?:cotuong:\/\/room\/|\/room\/)(\d{6})(?:\?([^\s#]+))?/i);
        if (deepMatch) {
            const params = new URLSearchParams(deepMatch[2] || "");
            let token = params.get("token");
            if (!token && params.get("deeplink")) {
                const nested = parseRoomInput(decodeURIComponent(params.get("deeplink")));
                token = nested.token;
            }
            return {
                roomId: deepMatch[1],
                token: token || null
            };
        }

        // 2. Hỗ trợ mã 6 chữ số (kể cả khi người dùng gõ #839201 hoặc có khoảng trắng)
        const digitsOnly = text.replace(/[\s#-]/g, "");
        if (/^\d{6}$/.test(digitsOnly)) {
            return { roomId: digitsOnly, token: null };
        }

        return { roomId: digitsOnly, token: null };
    }

    function buildDeepLink(room, token) {
        if (!room) return "";
        const suffix = room.mode === "private" && token ? `?token=${encodeURIComponent(token)}` : "";
        return `cotuong://room/${room.id}${suffix}`;
    }

    function buildShareUrl(room, token) {
        if (!room) return "";
        const deepLink = buildDeepLink(room, token);
        const base = `${location.origin}/room/${room.id}?deeplink=${encodeURIComponent(deepLink)}`;
        return room.mode === "private" && token ? `${base}&token=${encodeURIComponent(token)}` : base;
    }

    async function fetchRooms() {
        try {
            const data = await apiGet("/api/rooms");
            if (Array.isArray(data.rooms)) {
                rooms = data.rooms;
                localStorage.setItem(roomKey, JSON.stringify(rooms));
                renderRooms();
            }
        } catch (err) {
            console.warn("Fetch rooms error:", err);
        }
    }

    function formatTime(room) {
        return `${room.total} phút · ${room.move === "0" ? "Không giới hạn" : room.move + " giây/nước"}`;
    }

    function renderRooms() {
        const query = ($("#room-search")?.value || "").toLowerCase().trim();
        const visible = rooms.filter((room) => {
            const matchFilter =
                currentFilter === "all" ||
                (currentFilter === "private" ? room.mode === "private" : room.status === "waiting");
            const matchQuery = `${room.id} ${room.name} ${room.owner}`.toLowerCase().includes(query);
            return matchFilter && matchQuery;
        });

        $("#room-list").innerHTML = visible
            .map((room) => {
                const playing = room.status === "playing";
                const full = (room.playerCount || 1) >= (room.maxPlayers || 2);
                let action = "Vào bàn";
                if (playing) {
                    action = room.spectators ? "👁 Xem trận" : "Đang đấu (Kín)";
                } else if (full) {
                    action = room.spectators ? "👁 Xem bàn" : "Phòng đã đủ người";
                }

                const statusLabel = playing
                    ? `● Đang thi đấu (${room.playerCount || 2}/2)`
                    : `○ Đang đợi (${room.playerCount || 1}/2)`;

                return `<article class="room-card">
                    <div class="room-card-top">
                        <span class="room-badge ${room.mode === "private" ? "private" : ""}">${room.mode === "private" ? "🔒 Riêng tư" : "🔓 Công khai"}</span>
                        <span class="room-id-tag">Mã: ${escapeHtml(room.id)}</span>
                        <span class="room-status">${statusLabel}</span>
                    </div>
                    <h3 title="${escapeHtml(room.name)}">${escapeHtml(room.name)}</h3>
                    <div class="room-owner">Chủ phòng · ${escapeHtml(room.owner)}</div>
                    <div class="room-meta">
                        <span>⏱ ${formatTime(room)}</span>
                        <span>♟ ${room.side === "random" ? "Ngẫu nhiên" : room.side === "red" ? "Đỏ" : "Đen"}</span>
                    </div>
                    <button class="primary-button room-card-action ${playing ? "watch" : ""}" data-room-id="${escapeHtml(room.id)}">${action} <span>→</span></button>
                </article>`;
            })
            .join("");

        $("#empty-rooms").classList.toggle("hidden", visible.length > 0);
        $$("[data-room-id]").forEach((button) =>
            button.addEventListener("click", () => joinRoomByCode(button.dataset.roomId, { source: "card" }))
        );
    }

    function showLobby() {
        $("#lobby-player-name").textContent = display();
        $("#lobby-shell").classList.remove("hidden");
        $("#room-stage-bar")?.classList.add("hidden");
        $("#app").classList.add("hidden");

        // Đảm bảo trạng thái người chơi trên Server là Online (ở Sảnh chờ, không phải In-game)
        apiPost("/api/rooms/lobby").catch(() => {});
        window.dispatchEvent(new CustomEvent("room:enter-lobby"));
        fetchRooms();
    }

    function updateStageHeader(room, role) {
        const roleLabels = {
            host: "Chủ phòng (Host)",
            player2: "Kỳ thủ đối kháng",
            spectator: "Khán giả"
        };
        $("#active-room-name").textContent = room.name || `Phòng #${room.id}`;
        $("#active-room-code").textContent = `#${room.id}`;
        $("#active-room-role").textContent = roleLabels[role] || "Kỳ thủ";
        $("#active-room-deeplink").textContent = `Deep Link: ${buildDeepLink(room, currentInviteToken)}`;
    }

    function enterRoom(room, role, inviteToken = null) {
        currentRoom = room;
        if (inviteToken) currentInviteToken = inviteToken;

        $("#lobby-shell").classList.add("hidden");
        $("#create-room-modal").classList.add("hidden");
        $("#find-room-modal").classList.add("hidden");
        $("#room-access-modal").classList.add("hidden");
        $("#room-stage-bar")?.classList.remove("hidden");
        $("#app").classList.remove("hidden");

        updateStageHeader(room, role);
        localStorage.setItem("hongky_current_room", JSON.stringify({ id: room.id, name: room.name, role }));
        history.pushState({}, "", `/room/${room.id}`);

        window.dispatchEvent(
            new CustomEvent("room:entered", {
                detail: { room, role, inviteToken: currentInviteToken }
            })
        );
    }

    async function leaveCurrentRoom() {
        if (currentRoom) {
            const leavingId = currentRoom.id;
            try {
                await apiPost(`/api/rooms/leave/${encodeURIComponent(leavingId)}`);
            } catch (err) {
                console.warn("Leave room API error:", err);
            }
            window.dispatchEvent(new CustomEvent("room:left", { detail: { roomId: leavingId } }));
        }
        currentRoom = null;
        currentInviteToken = null;
        localStorage.removeItem("hongky_current_room");
        history.pushState({}, "", "/");
        showLobby();
    }

    function openPasswordModal(room) {
        currentRoom = room;
        $("#access-room-copy").textContent = `Phòng “${room.name}” (#${room.id}) là phòng riêng tư. Vui lòng nhập mật khẩu để vào bàn cờ.`;
        $("#access-password").value = "";
        $("#access-feedback").textContent = "";
        $("#room-access-modal").classList.remove("hidden");
        setTimeout(() => $("#access-password").focus(), 50);
    }

    // ====================================================
    // XỬ LÝ THAM GIA PHÒNG (Dùng chung cho cả 3 hình thức)
    // ====================================================
    async function joinRoomByCode(rawInput, options = {}) {
        const { password = null, inviteToken = null, source = "find" } = options;
        const parsed = parseRoomInput(rawInput);
        const roomId = parsed.roomId;
        const effectiveToken = inviteToken || parsed.token || null;

        const feedbackEl =
            source === "find"
                ? $("#find-room-feedback")
                : source === "password"
                ? $("#access-feedback")
                : $("#lobby-auth-feedback");

        if (feedbackEl) {
            feedbackEl.textContent = "";
            feedbackEl.classList.remove("success");
        }

        if (!/^\d{6}$/.test(roomId)) {
            const msg = "Không tìm thấy phòng";
            if (feedbackEl) feedbackEl.textContent = msg;
            return false;
        }

        try {
            const payload = { roomId };
            if (password !== null && password !== undefined && password !== "") {
                payload.password = password;
            }
            if (effectiveToken) {
                payload.inviteToken = effectiveToken;
            }

            const result = await apiPost("/api/rooms/join", payload);

            if (result.requirePassword && result.room) {
                $("#find-room-modal").classList.add("hidden");
                showLobby();
                openPasswordModal(result.room);
                return false;
            }

            if (result.succeeded && result.room) {
                enterRoom(result.room, result.role || "player2", effectiveToken);
                return true;
            }
        } catch (error) {
            if (source === "deeplink") {
                showLobby();
            }
            if (feedbackEl) {
                feedbackEl.textContent = error.message || "Không thể vào phòng.";
            } else {
                alert(error.message || "Không thể vào phòng.");
            }
            return false;
        }
        return false;
    }

    // ====================================================
    // HÌNH THỨC 1: MỜI BẠN BÈ TRONG GAME (IN-GAME INVITE)
    // ====================================================
    async function openFriendsModal() {
        if (!currentRoom) {
            alert("Hãy tạo hoặc vào một phòng trước khi mời bạn bè.");
            return;
        }
        $("#invite-modal").classList.add("hidden");
        $("#friends-room-badge").textContent = `#${currentRoom.id} (${currentRoom.name})`;
        $("#friends-feedback").textContent = "";
        $("#friends-feedback").classList.remove("success");
        $("#friends-modal").classList.remove("hidden");
        await loadFriendsList();
    }

    async function loadFriendsList() {
        const listEl = $("#friends-list");
        if (!listEl) return;
        listEl.innerHTML = `<div class="no-message">Đang tải danh sách bạn bè...</div>`;
        try {
            const data = await apiGet("/api/rooms/friends");
            const friends = data.friends || [];
            if (friends.length === 0) {
                listEl.innerHTML = `<div class="no-message">Chưa có bạn bè nào trong danh sách.</div>`;
                return;
            }

            listEl.innerHTML = friends
                .map((friend) => {
                    const statusKey =
                        friend.status === "Online"
                            ? "online"
                            : friend.status === "In-game"
                            ? "ingame"
                            : "offline";
                    const statusText =
                        friend.status === "Online"
                            ? "Online"
                            : friend.status === "In-game"
                            ? `In-game${friend.currentRoomId ? ` (#${friend.currentRoomId})` : ""}`
                            : "Offline";
                    const canInvite = friend.status === "Online";
                    const buttonLabel =
                        friend.status === "Online"
                            ? "Mời"
                            : friend.status === "In-game"
                            ? "Đang bận"
                            : "Offline";
                    const initial = (friend.displayName || "K").trim().charAt(0).toUpperCase();

                    return `<div class="friend-item">
                        <div class="friend-info">
                            <div class="friend-avatar">${escapeHtml(initial)}</div>
                            <div class="friend-meta">
                                <strong>${escapeHtml(friend.displayName)}</strong>
                                <span class="friend-status ${statusKey}"><i></i> ${escapeHtml(statusText)}</span>
                            </div>
                        </div>
                        <button type="button" class="friend-invite-btn" data-invite-user="${escapeHtml(friend.id)}" data-invite-name="${escapeHtml(friend.displayName)}" ${canInvite ? "" : "disabled"}>${buttonLabel}</button>
                    </div>`;
                })
                .join("");

            $$("[data-invite-user]").forEach((btn) => {
                btn.addEventListener("click", async () => {
                    if (!currentRoom) return;
                    const targetUserId = btn.dataset.inviteUser;
                    const targetName = btn.dataset.inviteName;
                    btn.disabled = true;
                    btn.textContent = "Đã mời ✓";
                    try {
                        window.dispatchEvent(
                            new CustomEvent("room:send-invite", {
                                detail: { targetUserId, roomId: currentRoom.id }
                            })
                        );
                        $("#friends-feedback").textContent = `Đã gửi lời mời real-time tới ${targetName} vào phòng #${currentRoom.id}.`;
                        $("#friends-feedback").classList.add("success");
                    } catch (err) {
                        $("#friends-feedback").textContent = err.message || "Không thể gửi lời mời.";
                        $("#friends-feedback").classList.remove("success");
                        btn.disabled = false;
                        btn.textContent = "Mời";
                    }
                });
            });
        } catch (err) {
            listEl.innerHTML = `<div class="no-message">Không thể tải danh sách bạn bè.</div>`;
        }
    }

    // Khi nhận lời mời real-time từ Host
    window.addEventListener("room:incoming-invite", (event) => {
        const payload = event.detail;
        if (!payload || !payload.roomId) return;
        pendingIncomingInvite = payload;
        $("#incoming-invite-text").textContent =
            payload.message || `${payload.hostName} mời bạn vào phòng ${payload.roomId}.`;
        $("#incoming-invite-modal").classList.remove("hidden");
    });

    // Khi nhận phản hồi lời mời từ bạn bè
    window.addEventListener("room:invite-response", (event) => {
        const resp = event.detail;
        if (!resp) return;
        if (!$("#friends-modal").classList.contains("hidden")) {
            $("#friends-feedback").textContent = resp.message;
            $("#friends-feedback").classList.toggle("success", !!resp.accepted);
        }
        const stageFeedback = $("#room-stage-feedback");
        if (stageFeedback && !$("#app").classList.contains("hidden")) {
            stageFeedback.textContent = resp.message;
            stageFeedback.classList.remove("hidden");
            setTimeout(() => stageFeedback.classList.add("hidden"), 5000);
        }
    });

    // Khi danh sách phòng hoặc trạng thái bạn bè cập nhật qua SignalR
    window.addEventListener("room:list-updated", (event) => {
        if (Array.isArray(event.detail)) {
            rooms = event.detail;
            renderRooms();
        } else {
            fetchRooms();
        }
    });

    window.addEventListener("room:presence-updated", () => {
        if (!$("#friends-modal").classList.contains("hidden")) {
            loadFriendsList();
        }
    });

    // ====================================================
    // HÌNH THỨC 2: MỜI QUA MẠNG XÃ HỘI (DEEP LINK)
    // ====================================================
    function openShareModal(room, inviteToken) {
        const targetRoom = room || currentRoom;
        if (!targetRoom) return;
        const token = inviteToken || currentInviteToken;
        const deepLink = buildDeepLink(targetRoom, token);
        const shareUrl = buildShareUrl(targetRoom, token);

        const codeDisplay = $("#share-room-code-display");
        if (codeDisplay) codeDisplay.textContent = targetRoom.id;
        $("#deeplink-input").value = deepLink;
        $("#invite-link-input").value = shareUrl;
        $("#invite-feedback").textContent = "";
        $("#invite-modal").classList.remove("hidden");
    }

    async function copyToClipboard(text, message) {
        try {
            await navigator.clipboard.writeText(text);
        } catch {
            const temp = document.createElement("input");
            temp.value = text;
            document.body.appendChild(temp);
            temp.select();
            document.execCommand("copy");
            temp.remove();
        }
        $("#invite-feedback").textContent = message;
    }

    $$("[data-share-channel]").forEach((btn) => {
        btn.addEventListener("click", async () => {
            if (!currentRoom) return;
            const channel = btn.dataset.shareChannel;
            const deepLink = $("#deeplink-input").value || buildDeepLink(currentRoom, currentInviteToken);
            const webUrl = $("#invite-link-input").value || buildShareUrl(currentRoom, currentInviteToken);
            const shareText = `${display()} mời bạn vào phòng Cờ Tướng #${currentRoom.id} (${currentRoom.name}). Mở link: ${webUrl} (Deep Link: ${deepLink})`;

            // Sao chép link web kèm deeplink để khi dán lên thanh địa chỉ trình duyệt hoặc Tìm phòng đều vào thẳng bàn cờ
            await copyToClipboard(
                webUrl,
                `Đã sao chép Link mời (${channel === "zalo" ? "Zalo" : channel === "facebook" ? "Facebook" : "Messenger"})! Bạn có thể gửi hoặc dán thẳng lên thanh địa chỉ trình duyệt.`
            );

            const encodedUrl = encodeURIComponent(webUrl);
            const encodedQuote = encodeURIComponent(shareText);
            if (channel === "facebook") {
                window.open(`https://www.facebook.com/sharer/sharer.php?u=${encodedUrl}&quote=${encodedQuote}`, "_blank", "noopener,width=620,height=480");
            } else if (channel === "zalo") {
                window.open(`https://sp.zalo.me/share_inline?d=${btoa(unescape(encodeURIComponent(JSON.stringify({ url: webUrl }))))}`, "_blank", "noopener,width=620,height=520");
            } else if (channel === "messenger") {
                window.open(`https://www.facebook.com/dialog/send?link=${encodedUrl}&app_id=966242223397117&redirect_uri=${encodedUrl}`, "_blank", "noopener,width=620,height=480");
            }
        });
    });

    // ====================================================
    // SỰ KIỆN GIAO DIỆN (DOM EVENTS)
    // ====================================================
    function openCreateModal() {
        const account = session();
        $("#room-name").value = `Phòng của ${account.displayName || "Kỳ thủ"}`;
        $("#room-password").value = "";
        $("#create-room-feedback").textContent = "";
        $("#create-room-modal").classList.remove("hidden");
    }

    function openFindRoomModal() {
        $("#find-room-code").value = "";
        $("#find-room-feedback").textContent = "";
        $("#find-room-modal").classList.remove("hidden");
        setTimeout(() => $("#find-room-code").focus(), 50);
    }

    $("#create-room-button").addEventListener("click", openCreateModal);
    $("#find-room-button").addEventListener("click", openFindRoomModal);

    // Khi gõ vào ô tìm kiếm ở Sảnh chờ, tự động đồng bộ danh sách phòng mới nhất từ Server
    $("#room-search").addEventListener("input", () => {
        renderRooms();
        clearTimeout(searchRefreshTimer);
        searchRefreshTimer = setTimeout(() => fetchRooms(), 200);
    });

    // Nếu người dùng gõ mã 6 chữ số hoặc Deep Link vào ô tìm kiếm ở Sảnh chờ rồi nhấn Enter -> Vào phòng luôn
    $("#room-search").addEventListener("keydown", async (event) => {
        if (event.key !== "Enter") return;
        const val = $("#room-search").value.trim();
        const parsed = parseRoomInput(val);
        if (/^\d{6}$/.test(parsed.roomId)) {
            event.preventDefault();
            await joinRoomByCode(val, { source: "card" });
        }
    });

    $$("[data-sample-code]").forEach((chip) => {
        chip.addEventListener("click", () => {
            $("#find-room-code").value = chip.dataset.sampleCode;
            $("#find-room-feedback").textContent = "";
            $("#find-room-code").focus();
        });
    });

    $$("[data-room-filter]").forEach((button) =>
        button.addEventListener("click", () => {
            currentFilter = button.dataset.roomFilter;
            $$("[data-room-filter]").forEach((item) => item.classList.toggle("active", item === button));
            renderRooms();
        })
    );

    $$("[data-close-room-modal]").forEach((button) =>
        button.addEventListener("click", () => $("#create-room-modal").classList.add("hidden"))
    );
    $$("[data-close-find-modal]").forEach((button) =>
        button.addEventListener("click", () => $("#find-room-modal").classList.add("hidden"))
    );
    $$("[data-close-access-modal]").forEach((button) =>
        button.addEventListener("click", () => $("#room-access-modal").classList.add("hidden"))
    );
    $$("[data-close-invite-modal]").forEach((button) =>
        button.addEventListener("click", () => $("#invite-modal").classList.add("hidden"))
    );
    $$("[data-close-friends-modal]").forEach((button) =>
        button.addEventListener("click", () => $("#friends-modal").classList.add("hidden"))
    );

    $$('input[name="room-mode"]').forEach((input) =>
        input.addEventListener("change", () => {
            $$(".mode-option").forEach((option) =>
                option.classList.toggle("active", option.querySelector("input").checked)
            );
            $("#private-options").classList.toggle("hidden", input.value !== "private" || !input.checked);
        })
    );

    // Tạo phòng mới
    $("#create-room-form").addEventListener("submit", async (event) => {
        event.preventDefault();
        const mode = $("input[name='room-mode']:checked").value;
        const password = $("#room-password").value.trim();
        if (mode === "private" && !/^[A-Za-z0-9]{4,6}$/.test(password)) {
            $("#create-room-feedback").textContent = "Phòng riêng tư cần mật khẩu gồm 4–6 chữ số hoặc ký tự, không dấu cách.";
            return;
        }

        try {
            const result = await apiPost("/api/rooms", {
                name: $("#room-name").value.trim() || `Phòng của ${display()}`,
                mode,
                password,
                total: $("#room-total-time").value,
                move: $("#room-move-time").value,
                side: $("#room-side").value,
                spectators: $("#room-spectators").checked
            });

            currentRoom = result.room;
            currentInviteToken = result.inviteToken;
            $("#create-room-modal").classList.add("hidden");
            await fetchRooms();
            enterRoom(result.room, "host", result.inviteToken);
            openShareModal(result.room, result.inviteToken);
        } catch (err) {
            $("#create-room-feedback").textContent = err.message || "Không thể tạo phòng.";
        }
    });

    // Hình thức 3: Submit form Tìm phòng bằng Mã 6 chữ số (hoặc Deep Link)
    $("#find-room-form").addEventListener("submit", async (event) => {
        event.preventDefault();
        const rawCode = $("#find-room-code").value.trim();
        if (!rawCode) {
            $("#find-room-feedback").textContent = "Không tìm thấy phòng";
            return;
        }
        await joinRoomByCode(rawCode, { source: "find" });
    });

    // Submit mật khẩu phòng riêng tư
    $("#room-access-form").addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!currentRoom) return;
        const password = $("#access-password").value;
        if (!password) {
            $("#access-feedback").textContent = "Mật khẩu phòng không chính xác";
            return;
        }
        await joinRoomByCode(currentRoom.id, { password, source: "password" });
    });

    // Nút sao chép Deep Link & Web Link
    $("#copy-deeplink").addEventListener("click", () => {
        copyToClipboard($("#deeplink-input").value, "Đã sao chép Mã Deep Link App (dán vào Tìm phòng).");
    });
    $("#copy-invite").addEventListener("click", () => {
        copyToClipboard($("#invite-link-input").value, "Đã sao chép Link mời (dán lên thanh địa chỉ trình duyệt hoặc gửi bạn bè).");
    });

    // Các nút trong Modal Chia sẻ & Thanh điều khiển Phòng đấu
    $("#invite-open-friends").addEventListener("click", openFriendsModal);
    $("#invite-enter-room").addEventListener("click", () => {
        $("#invite-modal").classList.add("hidden");
    });
    $("#room-invite-friends-btn").addEventListener("click", openFriendsModal);
    $("#room-share-social-btn").addEventListener("click", () => openShareModal(currentRoom, currentInviteToken));
    $("#leave-room-button").addEventListener("click", leaveCurrentRoom);

    // Chấp nhận / Từ chối lời mời real-time
    $("#accept-invite-button").addEventListener("click", async () => {
        if (!pendingIncomingInvite) return;
        const invite = pendingIncomingInvite;
        pendingIncomingInvite = null;
        $("#incoming-invite-modal").classList.add("hidden");

        window.dispatchEvent(
            new CustomEvent("room:respond-invite", {
                detail: { hostId: invite.hostId, roomId: invite.roomId, accepted: true }
            })
        );

        // Vào thẳng phòng chờ của Host, tự động bỏ qua mật khẩu nhờ inviteToken
        await joinRoomByCode(invite.roomId, {
            inviteToken: invite.inviteToken,
            source: "invite"
        });
    });

    $("#decline-invite-button").addEventListener("click", () => {
        if (!pendingIncomingInvite) return;
        const invite = pendingIncomingInvite;
        pendingIncomingInvite = null;
        $("#incoming-invite-modal").classList.add("hidden");

        window.dispatchEvent(
            new CustomEvent("room:respond-invite", {
                detail: { hostId: invite.hostId, roomId: invite.roomId, accepted: false }
            })
        );
    });

    $("#logout-button").addEventListener("click", async () => {
        const button = $("#logout-button");
        button.disabled = true;
        try {
            await window.hongKyAuth.post("logout");
            window.hongKyAuth.account = null;
            location.assign("/");
        } catch (error) {
            alert(error.message);
            button.disabled = false;
        }
    });

    // Khởi tạo sau khi đăng nhập (chống chạy trùng 2 lần giữa auth:success và auth.ready)
    let authHandledForUser = null;
    async function onAuthenticated(account) {
        if (!account) return;
        const userKey = account.id || account.username || account.displayName;
        if (authHandledForUser === userKey) return;
        authHandledForUser = userKey;

        const storedRaw = sessionStorage.getItem("hongky_pending_deeplink");
        if (storedRaw) {
            sessionStorage.removeItem("hongky_pending_deeplink");
            try {
                const pending = JSON.parse(storedRaw);
                if (pending && pending.roomId) {
                    const joined = await joinRoomByCode(pending.roomId, {
                        inviteToken: pending.token,
                        source: "deeplink"
                    });
                    if (joined) return;
                    return;
                }
            } catch {
                // Ignore parse error
            }
        }

        const currentTarget = detectInitialUrlTarget();
        if (currentTarget && currentTarget.roomId) {
            const joined = await joinRoomByCode(currentTarget.roomId, {
                inviteToken: currentTarget.token,
                source: "deeplink"
            });
            if (joined) return;
            return;
        }

        showLobby();
    }

    window.addEventListener("auth:success", (event) => {
        onAuthenticated(event.detail || window.hongKyAuth.account);
    });

    window.hongKyAuth.ready.then((account) => {
        if (account) onAuthenticated(account);
    });
})();
