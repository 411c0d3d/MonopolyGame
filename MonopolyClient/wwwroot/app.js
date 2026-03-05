/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, authService, React, ReactDOM, signalR */

// app.js — root component; depends on all other components.

/** Thin wrapper that supplies the toast context to the component tree. */
function AppProvider({value, children}) {
    return React.createElement(Ctx.Provider, {value}, children);
}

/** Root application component. Handles auth init, routing, and global hub subscriptions. */
function App() {
    const [page, setPage]           = useState('loading'); // 'loading' | 'login' | 'home' | 'lobby' | 'game' | 'admin'
    const [gameId, setGameId]       = useState('');
    const [gameState, setGameState] = useState(null);
    const [toasts, setToasts]       = useState([]);
    const [isCreator, setIsCreator] = useState(false);

    // Derived from Entra claims — never manually entered.
    const [user, setUser]       = useState(null); // { objectId, name, email }
    const [isAdmin, setIsAdmin] = useState(false);

    const toast = useCallback((msg, type = 'info') => {
        const id = Date.now() + Math.random();
        setToasts(prev => [...prev, {id, msg, type}]);
        setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 4000);
    }, []);

    // -------------------------------------------------------------------------
    // Auth init — runs once on mount
    // -------------------------------------------------------------------------
    useEffect(() => {
        authService.initialize().then(async account => {
            if (!account) {
                // Not authenticated — show login page
                setPage('login');
                return;
            }

            const currentUser = authService.getUser();
            setUser(currentUser);

            const admin = await authService.isAdmin();
            setIsAdmin(admin);

            // Start hub with JWT attached
            gameHub.start()
                .then(() => toast('Connected', 'success'))
                .catch(() => toast(`Cannot connect to ${SERVER_URL}`, 'error'));

            // Restore session game if user was in a game before refresh
            const savedGameId = authService.getSessionGame();
            if (savedGameId) {
                setGameId(savedGameId);
                gameHub.call('JoinGame', savedGameId)
                    .then(() => setPage('lobby'))
                    .catch(() => {
                        authService.clearSessionGame();
                        setPage('home');
                    });
            } else {
                setPage('home');
            }
        });

        const unsubscribers = [
            gameHub.on('GameStateUpdated', state => setGameState(state)),
            gameHub.on('Error',       msg => toast(msg, 'error')),
            gameHub.on('Reconnecting', () => toast('Reconnecting…', 'info')),
            gameHub.on('Reconnected',  () => {
                toast('Reconnected', 'success');
                // Rejoin group after transport reconnect
                if (gameId) { gameHub.call('JoinGame', gameId).catch(() => {}); }
            }),
            gameHub.on('Closed',       () => toast('Disconnected', 'error')),
            gameHub.on('TurnWarning', ({message}) => toast(`⏰ ${message}`, 'warning')),
            gameHub.on('Kicked',      msg => {
                toast(msg, 'error');
                handleLeave();
            }),
        ];

        return () => unsubscribers.forEach(fn => fn());
    }, []);

    // Auto-advance lobby → game when server transitions status
    useEffect(() => {
        if (gameState?.status === 'InProgress' && page === 'lobby') {
            setPage('game');
        }
    }, [gameState?.status, page]);

    // -------------------------------------------------------------------------
    // Handlers
    // -------------------------------------------------------------------------

    const handleCreateAndJoin = (gid) => {
        setGameId(gid);
        setIsCreator(true);
        authService.saveSessionGame(gid);
        gameHub.call('JoinGame', gid)
            .then(() => setPage('lobby'))
            .catch(e => toast(e.message || 'Failed to join', 'error'));
    };

    const handleJoin = (gid) => {
        setGameId(gid);
        setIsCreator(false);
        authService.saveSessionGame(gid);
        gameHub.call('JoinGame', gid)
            .then(() => setPage('lobby'))
            .catch(e => toast(e.message || 'Failed to join', 'error'));
    };

    const handleStart = () => {
        gameHub.call('StartGame', gameId).catch(e => toast(e.message || 'Could not start', 'error'));
    };

    const handleLeave = () => {
        setGameState(null);
        setGameId('');
        setIsCreator(false);
        authService.clearSessionGame();
        setPage('home');
    };

    // -------------------------------------------------------------------------
    // Render
    // -------------------------------------------------------------------------

    if (page === 'loading') {
        return (
            <div className="cover">
                <div className="spin" style={{width: 28, height: 28, borderWidth: 3}}/>
                <div style={{fontSize: 13, color: '#aaa'}}>Authenticating…</div>
            </div>
        );
    }

    if (page === 'login') {
        return <LoginPage onLogin={() => authService.login()}/>;
    }

    return (
        <AppProvider value={{toast}}>
            {page === 'home' && (
                <HomePage
                    user={user}
                    isAdmin={isAdmin}
                    onCreateAndJoin={handleCreateAndJoin}
                    onJoin={handleJoin}
                    onAdmin={() => setPage('admin')}
                    onLogout={() => authService.logout()}
                />
            )}
            {page === 'lobby' && (
                <LobbyPage
                    gameId={gameId}
                    userId={user?.objectId}
                    playerName={user?.name}
                    gameState={gameState}
                    onStart={handleStart}
                    onLeave={handleLeave}
                    isCreator={isCreator}
                    isAdmin={isAdmin}
                    onAdmin={() => setPage('admin')}
                />
            )}
            {page === 'game' && (
                <GamePage
                    gameId={gameId}
                    userId={user?.objectId}
                    playerName={user?.name}
                    gameState={gameState}
                    onLeave={handleLeave}
                    isAdmin={isAdmin}
                    onAdmin={() => setPage('admin')}
                />
            )}
            {page === 'admin' && (
                <AdminPage onBack={() => setPage(gameId ? 'game' : 'home')}/>
            )}
            <Toasts list={toasts}/>
        </AppProvider>
    );
}

const rootElement = document.getElementById('root');
const appRoot = ReactDOM.createRoot(rootElement);
appRoot.render(<App/>);