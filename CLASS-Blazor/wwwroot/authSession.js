window.authSession = {
    cookieName: "class_auth_token",

    setToken: function (token) {
        if (!token) {
            return;
        }

        var secure = window.location.protocol === "https:" ? "; Secure" : "";
        document.cookie = this.cookieName + "=" + encodeURIComponent(token) + "; Max-Age=2592000; Path=/; SameSite=Lax" + secure;
    },

    clearToken: function () {
        document.cookie = this.cookieName + "=; Max-Age=0; Path=/; SameSite=Lax";
    }
};
