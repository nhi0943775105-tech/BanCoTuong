(function () {
    const $ = (selector) => document.querySelector(selector);
    const $$ = (selector) => [...document.querySelectorAll(selector)];
    let otpState = null, countdownTimer = null;
    let pendingGoogleLink = false;
    const auth = window.hongKyAuth = { account: null, ready: null, post };
    // Remove old demo credentials. Authentication now uses the server's HttpOnly cookie.
    ["hongky_accounts", "hongky_session", "hongky_reset_token"].forEach((key) => localStorage.removeItem(key));

    function feedback(id, message, success = false) {
        $(id).textContent = message;
        $(id).classList.toggle("success", success);
    }

    async function readResponse(response) {
        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
            const message = data.errors ? Object.values(data.errors).flat().join(" ") : data.message;
            throw new Error(message || (response.status === 429
                ? "Bạn thao tác quá nhanh. Vui lòng thử lại sau một phút."
                : "Không thể xử lý yêu cầu. Vui lòng thử lại."));
        }
        return data;
    }

    async function post(path, body = {}) {
        // Refresh after identity changes to avoid stale antiforgery tokens.
        const csrf = await readResponse(await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" }));
        return readResponse(await fetch(`/api/auth/${path}`, {
            method: "POST", credentials: "same-origin",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": csrf.token },
            body: JSON.stringify(body)
        }));
    }

    async function submitting(form, action, feedbackId) {
        const button = form.querySelector('[type="submit"]');
        if (button.disabled) return;
        button.disabled = true;
        try { await action(); }
        catch (error) { feedback(feedbackId, error.message || "Không thể kết nối máy chủ."); }
        finally { button.disabled = false; }
    }

    function resetForgotForm() {
        $("#forgot-form").dataset.step = "";
        $("#forgot-heading").textContent = "Tìm lại tài khoản";
        $("#forgot-copy").textContent = "Nhập email đã đăng ký. Hiện dùng OTP demo 123456, chưa gửi email thật.";
        Object.assign($("#forgot-email"), { type: "email", placeholder: "you@example.com", autocomplete: "email", value: "" });
        $("#forgot-submit").textContent = "Lấy mã xác thực →";
    }

    function setView(view) {
        if (view !== "login") pendingGoogleLink = false;
        ["login", "register", "forgot"].forEach((name) => $(`#${name}-form`).classList.toggle("hidden", name !== view));
        $$(".auth-tab").forEach((tab) => tab.classList.toggle("active", tab.dataset.authView === view));
        $("#auth-title").textContent = { login: "Đăng nhập", register: "Tạo tài khoản", forgot: "Khôi phục mật khẩu" }[view];
        $("#auth-subtitle").textContent = { login: "Đăng nhập để tiếp tục vào sảnh chờ.", register: "Bắt đầu hành trình chinh phục bàn cờ.", forgot: "Lấy lại quyền truy cập tài khoản của bạn." }[view];
        $$(".social-divider, .social-buttons, .auth-legal").forEach((node) => node.classList.toggle("hidden", view === "forgot"));
        if (view === "forgot") resetForgotForm();
        ["#login-feedback", "#register-feedback", "#forgot-feedback"].forEach((id) => feedback(id, ""));
    }

    function enterLobby(account) {
        auth.account = account;
        $("#auth-shell").classList.add("hidden");
        $("#app").classList.add("hidden");
        ["#login-password", "#register-password", "#register-confirm"].forEach((id) => $(id).value = "");
        window.dispatchEvent(new CustomEvent("auth:success", { detail: account }));
    }

    function startCountdown(seconds) {
        clearInterval(countdownTimer);
        otpState.expiresAt = Date.now() + seconds * 1000;
        $("#resend-otp").disabled = true;
        const tick = () => {
            const remaining = Math.max(0, Math.ceil((otpState.expiresAt - Date.now()) / 1000));
            $("#otp-countdown").textContent = `${String(Math.floor(remaining / 60)).padStart(2, "0")}:${String(remaining % 60).padStart(2, "0")}`;
            if (!remaining) { clearInterval(countdownTimer); $("#resend-otp").disabled = false; }
        };
        tick(); countdownTimer = setInterval(tick, 250);
    }

    function openOtp(mode, email, seconds) {
        otpState = { mode, code: null };
        $("#otp-email").textContent = email;
        $("#otp-modal").classList.remove("hidden");
        feedback("#otp-feedback", "");
        $$(".otp-inputs input").forEach((input) => input.value = "");
        $$(".otp-inputs input")[0].focus();
        startCountdown(seconds);
    }

    function validateRegister() {
        const values = {
            username: $("#register-username").value.trim(), displayName: $("#register-display").value.trim(),
            email: $("#register-email").value.trim(), password: $("#register-password").value,
            confirmPassword: $("#register-confirm").value
        };
        $("#register-form").querySelectorAll(".field-error").forEach((node) => node.textContent = "");
        let valid = true;
        const invalid = (field, message) => { document.querySelector(`[data-error-for="${field}"]`).textContent = message; valid = false; };
        if (!/^[a-zA-Z0-9]{4,20}$/.test(values.username)) invalid("register-username", "4–20 ký tự, chỉ gồm chữ và số");
        if (values.displayName.length < 2 || values.displayName.length > 30) invalid("register-display", "Tên hiển thị từ 2–30 ký tự");
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(values.email)) invalid("register-email", "Vui lòng nhập email hợp lệ");
        if (values.password.length > 128 || !/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$/.test(values.password)) invalid("register-password", "Mật khẩu từ 8–128 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt");
        if (values.password !== values.confirmPassword) invalid("register-confirm", "Mật khẩu xác nhận không khớp");
        return valid ? values : null;
    }

    $("#login-form").addEventListener("submit", (event) => {
        event.preventDefault();
        submitting(event.currentTarget, async () => {
            const identity = $("#login-identity").value.trim(), password = $("#login-password").value;
            if (!identity || !password) throw new Error("Vui lòng nhập đầy đủ thông tin.");
            const result = await post(pendingGoogleLink ? "google/link" : "login", { identity, password, rememberMe: $("#remember-me").checked });
            pendingGoogleLink = false;
            enterLobby(result.account);
        }, "#login-feedback");
    });

    $("#register-form").addEventListener("submit", (event) => {
        event.preventDefault();
        const values = validateRegister();
        if (!values) return;
        submitting(event.currentTarget, async () => {
            feedback("#register-feedback", "");
            const result = await post("register", values);
            $("#register-password").value = ""; $("#register-confirm").value = "";
            openOtp("register", values.email, result.expiresIn);
        }, "#register-feedback");
    });

    $("#verify-otp").addEventListener("click", async () => {
        if (!otpState) return;
        const button = $("#verify-otp");
        button.disabled = true;
        try {
            const code = $$(".otp-inputs input").map((input) => input.value).join("");
            if (!/^\d{6}$/.test(code)) throw new Error("Vui lòng nhập đủ 6 chữ số.");
            const result = await post(otpState.mode === "register" ? "verify-registration" : "verify-reset-otp", { code });
            $("#otp-modal").classList.add("hidden"); clearInterval(countdownTimer);
            if (otpState.mode === "register") {
                otpState = null; enterLobby(result.account);
            } else {
                otpState.code = code;
                $("#forgot-form").dataset.step = "reset";
                $("#forgot-heading").textContent = "Tạo mật khẩu mới";
                $("#forgot-copy").textContent = "Mật khẩu cần ít nhất 8 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt.";
                Object.assign($("#forgot-email"), { type: "password", autocomplete: "new-password", placeholder: "Mật khẩu mới", value: "" });
                $("#forgot-submit").textContent = "Đặt lại mật khẩu →";
            }
        } catch (error) { feedback("#otp-feedback", error.message); }
        finally { button.disabled = false; }
    });

    $("#resend-otp").addEventListener("click", async () => {
        if (!otpState) return;
        $("#resend-otp").disabled = true;
        try {
            const result = await post(`resend-otp/${otpState.mode}`);
            startCountdown(result.expiresIn); feedback("#otp-feedback", result.message, true);
        } catch (error) { feedback("#otp-feedback", error.message); $("#resend-otp").disabled = false; }
    });

    $("#forgot-form").addEventListener("submit", (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        submitting(form, async () => {
            if (form.dataset.step === "reset") {
                const password = $("#forgot-email").value;
                await post("reset-password", { code: otpState?.code, password, confirmPassword: password });
                otpState = null; auth.account = null; resetForgotForm(); setView("login");
                feedback("#login-feedback", "Đặt lại mật khẩu thành công. Vui lòng đăng nhập.", true);
            } else {
                const email = $("#forgot-email").value.trim();
                const result = await post("forgot-password", { email });
                openOtp("reset", email, result.expiresIn);
            }
        }, "#forgot-feedback");
    });

    $("[data-close-modal]").addEventListener("click", () => { $("#otp-modal").classList.add("hidden"); clearInterval(countdownTimer); });
    $$(".otp-inputs input").forEach((input, index, inputs) => {
        input.addEventListener("input", () => {
            input.value = input.value.replace(/\D/g, "").slice(-1);
            if (input.value && inputs[index + 1]) inputs[index + 1].focus();
        });
        input.addEventListener("paste", (event) => {
            const code = event.clipboardData.getData("text").trim();
            if (!/^\d{6}$/.test(code)) return;
            event.preventDefault(); inputs.forEach((node, i) => node.value = code[i]); inputs[5].focus();
        });
    });
    $$("[data-auth-view]").forEach((button) => button.addEventListener("click", () => setView(button.dataset.authView)));
    $$("[data-toggle-password]").forEach((button) => button.addEventListener("click", () => {
        const input = $("#" + button.dataset.togglePassword);
        input.type = input.type === "password" ? "text" : "password";
        button.textContent = input.type === "password" ? "Hiện" : "Ẩn";
    }));
    $("#register-password").addEventListener("input", (event) => {
        const score = [/\S{8,}/, /[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z\d]/].filter((rule) => rule.test(event.target.value)).length;
        const meter = $(".password-meter i"); meter.style.width = `${score * 20}%`;
        meter.style.background = score < 3 ? "#bf4f45" : score < 5 ? "#c5963f" : "#2c8563";
    });
    $$("[data-provider]").forEach((button) => button.addEventListener("click", async () => {
        const target = $("#register-form").classList.contains("hidden") ? "#login-feedback" : "#register-feedback";
        if (button.dataset.provider !== "Google") {
            feedback(target, `Đăng nhập bằng ${button.dataset.provider} sẽ được bổ sung sau.`);
            return;
        }
        button.disabled = true;
        try {
            const csrf = await readResponse(await fetch("/api/auth/csrf", { credentials: "same-origin", cache: "no-store" }));
            // A normal form navigation allows the OAuth challenge to redirect to Google.
            const form = document.createElement("form");
            form.method = "POST";
            form.action = "/api/auth/google/start";
            form.hidden = true;
            for (const [name, value] of Object.entries({ __RequestVerificationToken: csrf.token, rememberMe: String($("#remember-me").checked) })) {
                const input = document.createElement("input");
                input.type = "hidden"; input.name = name; input.value = value;
                form.appendChild(input);
            }
            document.body.appendChild(form);
            form.submit();
        } catch (error) {
            feedback(target, error.message || "Không thể bắt đầu đăng nhập Google.");
            button.disabled = false;
        }
    }));

    const authQuery = new URLSearchParams(location.search);
    const authError = authQuery.get("authError");
    if (authError) {
        const messages = {
            google_not_configured: "Đăng nhập Google chưa được bật. Vui lòng dùng tài khoản và mật khẩu.",
            google_cancelled: "Bạn đã hủy đăng nhập Google. Có thể thử lại bất cứ lúc nào.",
            google_failed: "Không thể xác thực Google. Vui lòng thử lại.",
            google_account_failed: "Không thể tạo tài khoản từ Google. Vui lòng thử lại.",
            google_locked: "Tài khoản tạm khóa. Vui lòng thử lại sau 5 phút.",
            google_retry: "Tài khoản vừa được cập nhật. Vui lòng bấm Google để thử lại.",
            google_link_required: "Email Google đã có tài khoản. Nhập tên đăng nhập hoặc email và mật khẩu của tài khoản đó để liên kết Google."
        };
        pendingGoogleLink = authError === "google_link_required";
        feedback("#login-feedback", messages[authError] || "Không thể đăng nhập. Vui lòng thử lại.");
        authQuery.delete("authError");
        history.replaceState({}, "", location.pathname + (authQuery.size ? "?" + authQuery.toString() : "") + location.hash);
    }

    auth.ready = (async () => {
        $("#app").classList.add("hidden");
        try {
            const response = await fetch("/api/auth/me", { credentials: "same-origin", cache: "no-store" });
            if (response.status === 401) return null;
            const result = await readResponse(response); enterLobby(result.account); return result.account;
        } catch {
            feedback("#login-feedback", "Không thể kiểm tra phiên đăng nhập. Vui lòng kiểm tra kết nối máy chủ.");
            return null;
        }
    })();
})();
