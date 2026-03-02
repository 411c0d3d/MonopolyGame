/* globals useState, useEffect, useCallback, useContext, createContext, Ctx, COLORS, BCOLORS, SPACES, SERVER_URL, gameHub, React, ReactDOM, signalR */

// components/app.js — root component; depends on all other components.

/**
 * Thin wrapper that supplies the toast context to the component tree.
 * @param {{ value: any, children: any }} props
 */
function AppProvider({value, children}) {
    return React.createElement(Ctx.Provider, {value}, children);
}

/** Root application component managing page routing and global hub subscriptions. */
function App() {
    const [page, setPage] = useState('home');
    const [gameId, setGameId] = useState('');
    const [playerName, setPlayerName] = useState('');
    const [gameState, setGameState] = useState(null);
    const [loading, setLoading] = useState(true);
    const [toasts, setToasts] = useState([]);
    const [adminKey, setAdminKey] = useState('');
    const [isCreator, setIsCreator] = useState(false);

    const toast = useCallback((msg, type = 'info') => {
        const id = Date.now() + Math.random();
        setToasts(prev => [...prev, {id, msg, type}]);
        setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 4000);
    }, []);

    useEffect(() => {
        gameHub.start()
            .then(() => toast('Connected', 'success'))
            .catch(() => toast(`Cannot connect to ${SERVER_URL}`, 'error'))
            .finally(() => setLoading(false));

        const unsubscribers = [
            gameHub.on('GameStateUpdated', state => setGameState(state)),
            gameHub.on('Error', msg => toast(msg, 'error')),
            gameHub.on('Reconnecting', () => toast('Reconnecting…', 'info')),
            gameHub.on('Reconnected', () => toast('Reconnected', 'success')),
            gameHub.on('Closed', () => toast('Disconnected', 'error')),
            gameHub.on('TurnWarning', ({ message }) => toast(`⏰ ${message}`, 'warning')),
        ];

        return () => unsubscribers.forEach(fn => fn());
    }, []);

    useEffect(() => {
        if (gameState?.status === 'InProgress' && page === 'lobby') {
            setPage('game');
        }
    }, [gameState?.status, page]);

    const handleCreateAndJoin = (gid, name, botKey) => {
        setGameId(gid);
        setPlayerName(name);
        setIsCreator(true);
        if (botKey && !adminKey) { setAdminKey(botKey); }
        gameHub.call('JoinGame', gid, name)
            .then(() => setPage('lobby'))
            .catch(e => toast(e.message || 'Failed to join', 'error'));
    };

    const handleJoin = (gid, name) => {
        setGameId(gid);
        setPlayerName(name);
        setIsCreator(false);
        gameHub.call('JoinGame', gid, name)
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
        setPage('home');
    };

    const handleAdminLogin = (key) => {
        setAdminKey(key);
        setPage('admin');
    };

    return (
        <AppProvider value={{toast}}>
            {loading && (
                <div className="cover">
                    <div className="spin" style={{width: 28, height: 28, borderWidth: 3}}/>
                    <div style={{fontSize: 13, color: '#aaa'}}>Connecting to {SERVER_URL}…</div>
                </div>
            )}
            {!loading && page === 'home' && (
                <HomePage
                    onCreateAndJoin={handleCreateAndJoin}
                    onJoin={handleJoin}
                    onAdminLogin={handleAdminLogin}
                />
            )}
            {!loading && page === 'lobby' && (
                <LobbyPage
                    gameId={gameId}
                    playerName={playerName}
                    gameState={gameState}
                    onStart={handleStart}
                    onLeave={handleLeave}
                    isCreator={isCreator}
                    isAdmin={!!adminKey}
                    onAdmin={() => setPage('admin')}
                />
            )}
            {!loading && page === 'game' && (
                <GamePage
                    gameId={gameId}
                    playerName={playerName}
                    gameState={gameState}
                    onLeave={handleLeave}
                    isAdmin={!!adminKey}
                    onAdmin={() => setPage('admin')}
                    adminKey={adminKey}
                />
            )}
            {!loading && page === 'admin' && (
                <AdminPage adminKey={adminKey} onBack={() => setPage(gameId ? 'game' : 'home')}/>
            )}
            <Toasts list={toasts}/>
        </AppProvider>
    );
}

const rootElement = document.getElementById('root');
const appRoot = ReactDOM.createRoot(rootElement);
appRoot.render(<App/>);