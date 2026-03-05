/* globals msal, MSAL_CLIENT_ID, MSAL_AUTHORITY, MSAL_SCOPE */

// utils/auth_service.js — MSAL authentication wrapper for Microsoft Entra External ID.
// Handles login, logout, silent token renewal, admin role detection, and session restore.

const _scopes = ['openid', 'profile', 'email', MSAL_SCOPE];

/** Key used to persist last active game across page refreshes. */
const SESSION_GAME_KEY = 'monopoly_session_game';

class AuthService {
    constructor() {
        this._account = null;
        this._app = null;
    }

    /** Builds the MSAL config — called lazily so window.msal is guaranteed present. */
    _getApp() {
        if (this._app) {
            return this._app;
        }
        this._app = new window.msal.PublicClientApplication({
            auth: {
                clientId: MSAL_CLIENT_ID,
                authority: MSAL_AUTHORITY,
                redirectUri: window.location.origin,
                postLogoutRedirectUri: window.location.origin,
            },
            cache: {
                cacheLocation: 'sessionStorage',
                storeAuthStateInCookie: false,
            },
        });
        return this._app;
    }

    /**
     * Must be called once on app startup.
     * Handles the post-redirect callback and restores the cached account if present.
     * Returns the account if authenticated, null otherwise.
     */
    async initialize() {
        const app = this._getApp();
        await app.initialize();

        const result = await app.handleRedirectPromise();
        if (result?.account) {
            app.setActiveAccount(result.account);
            this._account = result.account;
        } else {
            const accounts = app.getAllAccounts();
            if (accounts.length > 0) {
                app.setActiveAccount(accounts[0]);
                this._account = accounts[0];
            }
        }

        return this._account;
    }

    /** Returns true if the user has an active authenticated session. */
    isAuthenticated() {
        return !!this._account;
    }

    /**
     * Returns the current user's identity from the cached account.
     * objectId is the B2C persistent identifier used as Player.Id server-side.
     */
    getUser() {
        if (!this._account) {
            return null;
        }
        return {
            objectId: this._account.localAccountId,
            name: this._account.name || this._account.username.split('@')[0],
            email: this._account.username,
        };
    }

    /**
     * Acquires an access token silently, falling back to a popup if interaction is required.
     * This token is passed to SignalR as ?access_token= on the WebSocket connection.
     */
    async getToken() {
        if (!this._account) {
            return null;
        }
        const app = this._getApp();
        try {
            const result = await app.acquireTokenSilent({scopes: _scopes, account: this._account});
            return result.accessToken;
        } catch (err) {
            if (err instanceof window.msal.InteractionRequiredAuthError) {
                try {
                    const result = await app.acquireTokenPopup({scopes: _scopes});
                    this._account = result.account;
                    app.setActiveAccount(result.account);
                    return result.accessToken;
                } catch {
                    return null;
                }
            }
            return null;
        }
    }

    /**
     * Decodes the JWT access token payload and checks the roles claim.
     * The Admin role is added by UserClaimsTransformation on the server.
     */
    async isAdmin() {
        const token = await this.getToken();
        if (!token) {
            return false;
        }
        try {
            const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
            const roles = payload.roles
                ?? payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
                ?? [];
            return Array.isArray(roles) ? roles.includes('Admin') : roles === 'Admin';
        } catch {
            return false;
        }
    }

    /** Triggers the Entra External ID login redirect flow. */
    login() {
        return this._getApp().loginRedirect({scopes: _scopes, prompt: 'login'});
    }

    /** Logs out and clears the session game state. */
    logout() {
        this.clearSessionGame();
        sessionStorage.clear();
        return this._getApp().logoutRedirect({account: this._account});
    }

    // -------------------------------------------------------------------------
    // Session game persistence — survives page refresh
    // -------------------------------------------------------------------------

    /** Saves the active game ID to sessionStorage so it survives a page refresh. */
    saveSessionGame(gameId) {
        sessionStorage.setItem(SESSION_GAME_KEY, gameId);
    }

    /** Returns the last active game ID, or null if none. */
    getSessionGame() {
        return sessionStorage.getItem(SESSION_GAME_KEY);
    }

    /** Clears the persisted game session (on leave or logout). */
    clearSessionGame() {
        sessionStorage.removeItem(SESSION_GAME_KEY);
    }
}

const authService = new AuthService();
window.authService = authService;