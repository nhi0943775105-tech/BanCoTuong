(function () {
    const $ = (selector) => document.querySelector(selector);
    const $$ = (selector) => [...document.querySelectorAll(selector)];
    const accountKey = "hongky_accounts";
    const sessionKey = "hongky_session";
    const otpDemo = "123456";
    let otpState = null;
    let countdownTimer = null;

    const accounts = () => JSON.parse(localStorage.getItem(accountKey) || "[]");
    const saveAccounts = (value) => localStorage.setItem(accountKey, JSON.stringify(value));
    const setError = (id, message) => { const node = document.querySelector(`[data-error-for="${id}"]`); if (node) node.textContent = message || ""; };
    const clearErrors = (form) => form.querySelectorAll(".field-error").forEach((node) => node.textContent = "");
    const feedback = (id, message, success = false) => { const node = $(id); if (node) { node.textContent = message; node.classList.toggle("success", success); } };

    function setView(view) {
        const login = $("#login-form"), register = $("#register-form"), forgot = $("#forgot-form");
        [login, register, forgot].forEach((form) => form.classList.add("hidden"));
        $$(".auth-tab").forEach((tab) => tab.classList.toggle("active", tab.dataset.authView === view));
        if (view === "login") { login.classList.remove("hidden"); $("#auth-title").textContent = "Đăng nhập"; $("#auth-subtitle").textContent = "Đăng nhập để tiếp tục vào sảnh chờ."; }
        if (view === "register") { register.classList.remove("hidden"); $("#auth-title").textContent = "Tạo tài khoản"; $("#auth-subtitle").textContent = "Bắt đầu hành trình chinh phục bàn cờ."; }
        if (view === "forgot") { forgot.classList.remove("hidden"); $("#auth-title").textContent = "Khôi phục mật khẩu"; $("#auth-subtitle").textContent = "Lấy lại quyền truy cập tài khoản của bạn."; $(".social-divider, .social-buttons, .auth-legal").forEach((node) => node.classList.add("hidden")); }
        if (view !== "forgot") $(".social-divider, .social-buttons, .auth-legal").forEach((node) => node.classList.remove("hidden"));
    }

    function enterLobby(account) {
        localStorage.setItem(sessionKey, JSON.stringify({ username: account.username, displayName: account.displayName, loggedInAt: Date.now() }));
        $("#auth-shell").classList.add("hidden"); $("#app").classList.add("hidden");
        window.dispatchEvent(new CustomEvent("auth:success", { detail: account }));
    }

    function openOtp(mode, account, email) {
        otpState = { mode, account, email, attempts: 0, expiresAt: Date.now() + 120000 };
        $("#otp-email").textContent = email; $("#otp-modal").classList.remove("hidden"); $("#otp-feedback").textContent = ""; $("#otp-feedback").classList.remove("success");
        $$(".otp-inputs input").forEach((input) => input.value = ""); $$(".otp-inputs input")[0].focus();
        startCountdown();
    }

    function startCountdown() {
        clearInterval(countdownTimer); $("#resend-otp").disabled = true;
        const tick = () => { const remaining = Math.max(0, (otpState?.expiresAt || Date.now()) - Date.now()); $("#otp-countdown").textContent = `${String(Math.floor(remaining / 60000)).padStart(2, "0")}:${String(Math.floor((remaining % 60000) / 1000)).padStart(2, "0")}`; if (!remaining) { clearInterval(countdownTimer); $("#resend-otp").disabled = false; } };
        tick(); countdownTimer = setInterval(tick, 250);
    }

    function validateRegister() {
        const values = { username: $("#register-username").value.trim(), display: $("#register-display").value.trim(), email: $("#register-email").value.trim(), password: $("#register-password").value, confirm: $("#register-confirm").value };
        let valid = true; clearErrors($("#register-form"));
        if (!/^[a-zA-Z0-9]{4,20}$/.test(values.username)) { setError("register-username", "4–20 ký tự, chỉ gồm chữ và số"); valid = false; }
        if (values.display.length < 2 || values.display.length > 30) { setError("register-display", "Tên hiển thị từ 2–30 ký tự"); valid = false; }
        if (!/^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/.test(values.email)) { setError("register-email", "Vui lòng nhập email hợp lệ"); valid = false; }
        if (!/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$/.test(values.password)) { setError("register-password", "Mật khẩu chưa đủ độ mạnh"); valid = false; }
        if (values.password !== values.confirm) { setError("register-confirm", "Mật khẩu xác nhận không khớp"); valid = false; }
        const existing = accounts(); if (existing.some((a) => a.username.toLowerCase() === values.username.toLowerCase())) { setError("register-username", "Tên đăng nhập đã được sử dụng"); valid = false; } if (existing.some((a) => a.email.toLowerCase() === values.email.toLowerCase())) { setError("register-email", "Email này đã được đăng ký"); valid = false; }
        return valid ? values : null;
    }

    function verifyOtp() {
        if (!otpState) return; const code = $$(".otp-inputs input").map((input) => input.value).join("");
        if (Date.now() > otpState.expiresAt) { feedback("#otp-feedback", "Mã OTP đã hết hạn, vui lòng gửi lại mã."); return; }
        if (code !== otpDemo) { otpState.attempts++; feedback("#otp-feedback", otpState.attempts >= 5 ? "Bạn đã nhập sai quá 5 lần. Vui lòng gửi lại mã." : "Mã xác nhận không chính xác, vui lòng thử lại"); if (otpState.attempts >= 5) $("#verify-otp").disabled = true; return; }
        const state = otpState; $("#otp-modal").classList.add("hidden"); clearInterval(countdownTimer); $("#verify-otp").disabled = false;
        if (state.mode === "register") { const next = [...accounts(), { ...state.account, active: true, lastPassword: state.account.password }]; saveAccounts(next); feedback("#register-feedback", "Đăng ký thành công!", true); setTimeout(() => enterLobby(state.account), 650); }
        if (state.mode === "reset") { localStorage.setItem("hongky_reset_token", JSON.stringify({ email: state.email, expiresAt: Date.now() + 600000 })); $("#forgot-heading").textContent = "Tạo mật khẩu mới"; $("#forgot-copy").textContent = "Mật khẩu mới cần đủ độ mạnh và khác mật khẩu gần nhất."; $("#forgot-form").dataset.step = "reset"; $("#forgot-email").type = "password"; $("#forgot-email").placeholder = "Mật khẩu mới"; $("#forgot-email").value = ""; $("#forgot-submit").textContent = "Đặt lại mật khẩu →"; $("#auth-title").textContent = "Đổi mật khẩu"; $("#auth-subtitle").textContent = "Thiết lập lại mật khẩu an toàn."; }
    }

    $("#login-form").addEventListener("submit", (event) => { event.preventDefault(); clearErrors(event.currentTarget); const identity = $("#login-identity").value.trim(), password = $("#login-password").value; if (!identity || !password) { feedback("#login-feedback", "Vui lòng nhập đầy đủ thông tin"); return; } const account = accounts().find((a) => a.email.toLowerCase() === identity.toLowerCase() || a.username.toLowerCase() === identity.toLowerCase()); if (!account || account.password !== password) { feedback("#login-feedback", "Tên đăng nhập hoặc mật khẩu không chính xác"); return; } if (!account.active) { openOtp("register", account, account.email); return; } enterLobby(account); });
    $("#register-form").addEventListener("submit", (event) => { event.preventDefault(); const values = validateRegister(); if (values) openOtp("register", values, values.email); });
    $("#forgot-form").addEventListener("submit", (event) => { event.preventDefault(); const form = event.currentTarget; if (form.dataset.step === "reset") { const password = $("#forgot-email").value; if (!/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).{8,}$/.test(password)) { feedback("#forgot-feedback", "Mật khẩu chưa đủ độ mạnh"); return; } const email = JSON.parse(localStorage.getItem("hongky_reset_token") || "{}").email; const list = accounts(); const account = list.find((a) => a.email === email); if (!account || account.password === password) { feedback("#forgot-feedback", "Mật khẩu mới không được trùng mật khẩu gần nhất"); return; } account.password = password; saveAccounts(list); feedback("#forgot-feedback", "Đặt lại mật khẩu thành công!", true); setTimeout(() => { form.dataset.step = ""; setView("login"); }, 800); return; } const email = $("#forgot-email").value.trim(); const account = accounts().find((a) => a.email.toLowerCase() === email.toLowerCase()); if (!account) { feedback("#forgot-feedback", "Email này chưa được đăng ký trong hệ thống"); return; } openOtp("reset", account, email); });
    $("#verify-otp").addEventListener("click", verifyOtp); $("#resend-otp").addEventListener("click", () => { if (otpState) { otpState.expiresAt = Date.now() + 120000; otpState.attempts = 0; $("#verify-otp").disabled = false; feedback("#otp-feedback", "Đã gửi mã mới.", true); startCountdown(); } }); $("[data-close-modal]").addEventListener("click", () => $("#otp-modal").classList.add("hidden"));
    $$(".otp-inputs input").forEach((input, index, inputs) => input.addEventListener("input", () => { input.value = input.value.replace(/\D/g, "").slice(-1); if (input.value && inputs[index + 1]) inputs[index + 1].focus(); }));
    $$("[data-auth-view]").forEach((button) => button.addEventListener("click", () => setView(button.dataset.authView))); $$("[data-toggle-password]").forEach((button) => button.addEventListener("click", () => { const input = $("#" + button.dataset.togglePassword); input.type = input.type === "password" ? "text" : "password"; button.textContent = input.type === "password" ? "Hiện" : "Ẩn"; }));
    $("#register-password").addEventListener("input", (event) => { const score = [/\S{8,}/, /[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z\d]/].filter((rule) => rule.test(event.target.value)).length; const meter = $(".password-meter i"); meter.style.width = `${score * 20}%`; meter.style.background = score < 3 ? "#bf4f45" : score < 5 ? "#c5963f" : "#2c8563"; });
    $$("[data-provider]").forEach((button) => button.addEventListener("click", () => { const provider = button.dataset.provider, socialAccount = { username: provider.toLowerCase().replace(/\s/g, "") + "_player", email: provider.toLowerCase().replace(/\s/g, "") + "@oauth.demo", displayName: "", password: "oauth-token", active: true }; $("#display-modal").classList.remove("hidden"); $("#social-display").value = ""; $("#save-display").dataset.account = JSON.stringify(socialAccount); feedback("#display-feedback", ""); }));
    $("#save-display").addEventListener("click", () => { const displayName = $("#social-display").value.trim(); if (displayName.length < 2 || displayName.length > 30) { feedback("#display-feedback", "Tên hiển thị từ 2–30 ký tự"); return; } const account = { ...JSON.parse($("#save-display").dataset.account), displayName }; $("#display-modal").classList.add("hidden"); enterLobby(account); });
    if (localStorage.getItem(sessionKey)) { $("#auth-shell").classList.add("hidden"); } else { $("#app").classList.add("hidden"); }
})();
