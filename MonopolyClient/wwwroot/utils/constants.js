/* globals React */
// utils/constants.js — loaded first; defines all shared app globals.

const SERVER_URL = 'http://localhost:5500';

// ---------------------------------------------------------------------------
// Auth — Microsoft Entra External ID (CIAM)
// Update these values to match your app registration.
// ---------------------------------------------------------------------------

/** App registration client ID from the Entra External ID tenant. */
window.MSAL_CLIENT_ID = '962f7297-974f-4752-bda4-7b1947a75ce1';

/** CIAM authority URL — https://{tenant}.ciamlogin.com/{tenantId}/v2.0 */
window.MSAL_AUTHORITY = 'https://12384f87-3250-4fe6-b8c4-a2b5a6692d7e.ciamlogin.com/12384f87-3250-4fe6-b8c4-a2b5a6692d7e/v2.0';

/**
 * OAuth scope requested for the access token sent to the SignalR hub.
 * Must match the scope exposed on the app registration (Expose an API → Add a scope).
 * Default: openid + profile + email covers basic OIDC claims.
 * For a proper access token targeting the server add: api://{clientId}/access_as_user
 */
window.MSAL_SCOPE = `api://${window.MSAL_CLIENT_ID}/access_as_user`;

// ---------------------------------------------------------------------------
// Game constants
// ---------------------------------------------------------------------------

/** Player token colors, one per player slot (up to 8). */
window.COLORS = [
    '#e74c3c', '#3498db', '#2ecc71', '#f39c12',
    '#9b59b6', '#1abc9c', '#e67e22', '#34495e',
];

/** Property group colors keyed by the group name used in SPACES. */
window.BCOLORS = {
    Brown: '#8B4513',
    LightBlue: '#ADD8E6',
    Pink: '#FF69B4',
    Orange: '#FFA500',
    Red: '#FF0000',
    Yellow: '#FFD700',
    Green: '#228B22',
    DarkBlue: '#00008B',
};

/** Board space definitions, index === board position. */
window.SPACES = [
    {id: 0, name: 'GO', type: 'Go'},
    {id: 1, name: 'Mediterranean', type: 'Street', color: 'Brown'},
    {id: 2, name: 'Community\nChest', type: 'CommunityChest'},
    {id: 3, name: 'Baltic', type: 'Street', color: 'Brown'},
    {id: 4, name: 'Income Tax', type: 'Tax'},
    {id: 5, name: 'Reading RR', type: 'Railroad'},
    {id: 6, name: 'Oriental', type: 'Street', color: 'LightBlue'},
    {id: 7, name: 'Chance', type: 'Chance'},
    {id: 8, name: 'Vermont', type: 'Street', color: 'LightBlue'},
    {id: 9, name: 'Connecticut', type: 'Street', color: 'LightBlue'},
    {id: 10, name: 'Jail', type: 'Jail'},
    {id: 11, name: 'St. Charles', type: 'Street', color: 'Pink'},
    {id: 12, name: 'Electric Co.', type: 'Utility'},
    {id: 13, name: 'States', type: 'Street', color: 'Pink'},
    {id: 14, name: 'Virginia', type: 'Street', color: 'Pink'},
    {id: 15, name: 'Pennsylvania RR', type: 'Railroad'},
    {id: 16, name: 'St. James', type: 'Street', color: 'Orange'},
    {id: 17, name: 'Community\nChest', type: 'CommunityChest'},
    {id: 18, name: 'Tennessee', type: 'Street', color: 'Orange'},
    {id: 19, name: 'New York', type: 'Street', color: 'Orange'},
    {id: 20, name: 'Free\nParking', type: 'FreeParking'},
    {id: 21, name: 'Kentucky', type: 'Street', color: 'Red'},
    {id: 22, name: 'Chance', type: 'Chance'},
    {id: 23, name: 'Indiana', type: 'Street', color: 'Red'},
    {id: 24, name: 'Illinois', type: 'Street', color: 'Red'},
    {id: 25, name: 'B&O RR', type: 'Railroad'},
    {id: 26, name: 'Atlantic', type: 'Street', color: 'Yellow'},
    {id: 27, name: 'Ventnor', type: 'Street', color: 'Yellow'},
    {id: 28, name: 'Water Works', type: 'Utility'},
    {id: 29, name: 'Marvin Gdns', type: 'Street', color: 'Yellow'},
    {id: 30, name: 'Go To Jail', type: 'GoToJail'},
    {id: 31, name: 'Pacific', type: 'Street', color: 'Green'},
    {id: 32, name: 'N. Carolina', type: 'Street', color: 'Green'},
    {id: 33, name: 'Community\nChest', type: 'CommunityChest'},
    {id: 34, name: 'Pennsylvania', type: 'Street', color: 'Green'},
    {id: 35, name: 'Short Line RR', type: 'Railroad'},
    {id: 36, name: 'Chance', type: 'Chance'},
    {id: 37, name: 'Park Place', type: 'Street', color: 'DarkBlue'},
    {id: 38, name: 'Luxury Tax', type: 'Tax'},
    {id: 39, name: 'Boardwalk', type: 'Street', color: 'DarkBlue'},
];

// ---------------------------------------------------------------------------
// React hooks — exposed as globals so every component file can use them
// without imports (Babel UMD build, no module system).
// ---------------------------------------------------------------------------

const {
    useState,
    useEffect,
    useCallback,
    useContext,
    useRef,
    useReducer,
    useMemo,
    createContext,
} = React;

window.useState = useState;
window.useEffect = useEffect;
window.useCallback = useCallback;
window.useContext = useContext;
window.useRef = useRef;
window.useReducer = useReducer;
window.useMemo = useMemo;
window.createContext = createContext;

/** Shared React context providing the toast() function to all components. */
window.Ctx = createContext(null);