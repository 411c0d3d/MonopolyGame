/* globals signalR, SERVER_URL */

// utils/hub_service.js

class HubService {
    constructor() {
        this._conn = null;
        this._handlers = {};
    }

    start() {
        this._conn = new signalR.HubConnectionBuilder()
            .withUrl(`${SERVER_URL}/game-hub`)
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        const events = [
            'GameStateUpdated',
            'Error',
            'TradeProposed',
            'GameForceEnded',
            'GamePaused',
            'GameResumed',
            'PlayerKicked',
            'Kicked',
            'TurnWarning',
            'GameCreated',
            'GameRemoved',
            'DiceRolled',
            'CardDrawn',
        ];

        events.forEach(evt => {
            this._conn.on(evt, (...args) => {
                this._fire(evt, ...args);
            });
        });

        this._conn.onreconnecting(() => {
            this._fire('Reconnecting');
        });

        this._conn.onreconnected(() => {
            this._fire('Reconnected');
        });

        this._conn.onclose(() => {
            this._fire('Closed');
        });

        return this._conn.start();
    }

    on(event, fn) {
        if (!this._handlers[event]) {
            this._handlers[event] = [];
        }

        this._handlers[event].push(fn);

        return () => {
            this._handlers[event] =
                this._handlers[event].filter(f => f !== fn);
        };
    }

    call(method, ...args) {
        if (!this._conn) {
            return Promise.reject('Not connected');
        }

        return this._conn.invoke(method, ...args);
    }

    _fire(event, ...args) {
        const handlers = this._handlers[event] || [];

        handlers.forEach(fn => {
            fn(...args);
        });
    }
}

const gameHub = new HubService();

window.gameHub = gameHub;