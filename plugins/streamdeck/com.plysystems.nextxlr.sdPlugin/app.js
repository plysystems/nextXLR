/**
 * NEXT : XLR — Official Elgato Stream Deck Plugin
 * (c) 2026 plysystems. All rights reserved.
 */

let websocket = null;
let pluginUUID = null;
let activeApiPort = 48500;
let lastMixerState = null;
let isEngineOnline = false;

// Active actions registered in the Stream Deck UI
const activeContexts = new Map();

// Candidate ports for NextXLR Engine API
const PORTS = [48500, 14565];

// --- Engine API Communication ---

async function detectApiPort() {
    for (const p of PORTS) {
        try {
            const res = await fetch(`http://127.0.0.1:${p}/health`, { method: "GET", signal: AbortSignal.timeout(600) });
            if (res.ok) {
                activeApiPort = p;
                isEngineOnline = true;
                return p;
            }
        } catch (e) {
            // Next candidate
        }
    }
    isEngineOnline = false;
    return activeApiPort;
}

async function callEngineApi(endpoint, params = {}) {
    await detectApiPort();
    const query = new URLSearchParams(params).toString();
    const url = `http://127.0.0.1:${activeApiPort}${endpoint}${query ? "?" + query : ""}`;
    try {
        const res = await fetch(url, { method: "GET", signal: AbortSignal.timeout(1200) });
        return await res.json();
    } catch (e) {
        console.warn(`Engine API request failed (${url}):`, e);
        return { ok: false, error: e.toString() };
    }
}

let engineWs = null;
let engineWsReconnectTimer = null;

function connectEngineWebSocket() {
    if (engineWs && (engineWs.readyState === WebSocket.OPEN || engineWs.readyState === WebSocket.CONNECTING)) {
        return;
    }
    try {
        engineWs = new WebSocket(`ws://127.0.0.1:${activeApiPort}/ws`);
        engineWs.onopen = () => {
            isEngineOnline = true;
            try { engineWs.send(JSON.stringify({ type: "get_state" })); } catch (e) { }
        };
        engineWs.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                if (msg.type === "state") {
                    lastMixerState = msg;
                    isEngineOnline = true;
                    updateAllActiveContexts();
                }
            } catch (err) { }
        };
        engineWs.onclose = () => {
            scheduleEngineWsReconnect();
        };
        engineWs.onerror = () => {
            try { engineWs.close(); } catch (e) { }
        };
    } catch (e) {
        scheduleEngineWsReconnect();
    }
}

function scheduleEngineWsReconnect() {
    if (!engineWsReconnectTimer) {
        engineWsReconnectTimer = setTimeout(() => {
            engineWsReconnectTimer = null;
            detectApiPort().then(() => connectEngineWebSocket());
        }, 2500);
    }
}

async function refreshEngineState() {
    connectEngineWebSocket();
    const res = await callEngineApi("/api/status");
    if (res && res.ok && res.state) {
        lastMixerState = res.state;
        if (res.active_page !== undefined) {
            lastMixerState.active_page = res.active_page;
        }
        if (res.fader_target_submix !== undefined) {
            lastMixerState.fader_target_submix = res.fader_target_submix;
        }
        isEngineOnline = true;
        updateAllActiveContexts();
    } else if (!engineWs || engineWs.readyState !== WebSocket.OPEN) {
        isEngineOnline = false;
    }
}

// Background heartbeat fallback loop (every 4s)
setInterval(() => {
    if (activeContexts.size > 0) {
        if (!engineWs || engineWs.readyState !== WebSocket.OPEN) {
            refreshEngineState();
        }
    }
}, 4000);

// --- Stream Deck WebSocket Setup ---

function connectElgatoStreamDeckSocket(inPort, inPluginUUID, inRegisterEvent, inInfo, inActionInfo) {
    pluginUUID = inPluginUUID;
    websocket = new WebSocket("ws://127.0.0.1:" + inPort);

    websocket.onopen = function () {
        const json = {
            event: inRegisterEvent,
            uuid: inPluginUUID
        };
        websocket.send(JSON.stringify(json));
        detectApiPort().then(refreshEngineState);
    };

    websocket.onmessage = function (evt) {
        try {
            const data = JSON.parse(evt.data);
            handleStreamDeckEvent(data);
        } catch (err) {
            console.error("Error parsing message from Stream Deck:", err);
        }
    };

    websocket.onclose = function () {
        console.log("Stream Deck WebSocket connection closed");
    };
}

// Helper methods to send messages to Stream Deck
function sendToStreamDeck(event, context, payload = {}) {
    if (websocket && websocket.readyState === WebSocket.OPEN) {
        websocket.send(JSON.stringify({ event, context, ...payload }));
    }
}

function setTitle(context, title) {
    sendToStreamDeck("setTitle", context, { payload: { title, target: 0 } });
}

function setState(context, state) {
    sendToStreamDeck("setState", context, { payload: { state } });
}

function showAlert(context) {
    sendToStreamDeck("showAlert", context);
}

function showOk(context) {
    sendToStreamDeck("showOk", context);
}

function setFeedback(context, feedback) {
    sendToStreamDeck("setFeedback", context, { payload: feedback });
}

// --- Event Handling ---

function handleStreamDeckEvent(msg) {
    const event = msg.event;
    const action = msg.action;
    const context = msg.context;
    const payload = msg.payload || {};
    const settings = payload.settings || {};

    switch (event) {
        case "willAppear":
            activeContexts.set(context, { action, settings });
            updateContextDisplay(context, action, settings);
            break;

        case "willDisappear":
            activeContexts.delete(context);
            break;

        case "didReceiveSettings":
            if (activeContexts.has(context)) {
                activeContexts.get(context).settings = settings;
            }
            updateContextDisplay(context, action, settings);
            break;

        case "sendToPlugin":
            // Message from Property Inspector
            if (payload.action === "refresh") {
                detectApiPort().then(refreshEngineState);
            }
            break;

        case "keyDown":
            handleKeyDown(context, action, settings);
            break;

        case "keyUp":
            handleKeyUp(context, action, settings);
            break;

        case "dialRotate":
            handleDialRotate(context, action, settings, payload.ticks || 0);
            break;

        case "dialPress":
            if (payload.pressed === false) {
                // Dial release -> toggle mute
                handleDialPress(context, action, settings);
            }
            break;

        case "touchTap":
            // Stream Deck + LCD tapped -> reset volume to 100%
            handleTouchTap(context, action, settings);
            break;
    }
}

// --- Action Logic ---

async function handleKeyDown(context, action, settings) {
    const channel = settings.channel || "music";

    switch (action) {
        case "com.plysystems.nextxlr.encoder":
        case "com.plysystems.nextxlr.fader": {
            const mode = settings.mode || "increase";
            const step = parseFloat(settings.step || "0.05");
            let res;
            if (mode === "increase") {
                res = await callEngineApi("/api/fader", { channel, delta: step });
            } else if (mode === "decrease") {
                res = await callEngineApi("/api/fader", { channel, delta: -step });
            } else if (mode === "set") {
                const vol = parseFloat(settings.volume || "0.75");
                res = await callEngineApi("/api/fader", { channel, volume: vol });
            } else if (mode === "toggle_mute") {
                res = await callEngineApi("/api/mute", { channel, action: "toggle" });
            }
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.page": {
            const mode = settings.mode || "toggle";
            let res;
            if (mode === "toggle") {
                res = await callEngineApi("/api/page", { action: "toggle" });
            } else {
                res = await callEngineApi("/api/page", { page: parseInt(mode, 10) });
            }
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.fadermute": {
            const fader = (settings.fader || "A").toUpperCase();
            const mode = settings.mode || "toggle";
            const res = await callEngineApi("/api/mute", { channel: fader, action: mode });
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.mixmode": {
            const target = settings.target || "toggle";
            let res;
            if (target === "toggle") {
                res = await callEngineApi("/api/mix-mode", { action: "toggle" });
            } else {
                res = await callEngineApi("/api/mix-mode", { target });
            }
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.mute": {
            const mode = settings.mode || "toggle";
            const res = await callEngineApi("/api/mute", { channel, action: mode });
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.micmute": {
            const mode = settings.mode || "toggle";
            let res;
            if (mode === "toggle") {
                res = await callEngineApi("/api/mic-mute", { action: "toggle" });
            } else {
                // Push-to-mute
                res = await callEngineApi("/api/mic-mute", { muted: true });
            }
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.bleep": {
            const mode = settings.mode || "hold";
            if (mode === "toggle") {
                const isBleeping = lastMixerState ? (lastMixerState.bleep || lastMixerState.bleep_active) : false;
                await callEngineApi("/api/bleep", { active: !isBleeping });
            } else {
                await callEngineApi("/api/bleep", { active: true });
            }
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.profile": {
            const profileName = settings.profileName || "Gaming";
            const res = await callEngineApi("/api/profile", { name: profileName });
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.route": {
            const bus = settings.bus || "stream";
            const res = await callEngineApi("/api/route", { channel, bus, action: "toggle" });
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }
    }
}

async function handleKeyUp(context, action, settings) {
    switch (action) {
        case "com.plysystems.nextxlr.bleep": {
            const mode = settings.mode || "hold";
            if (mode === "hold") {
                await callEngineApi("/api/bleep", { active: false });
                await refreshEngineState();
            }
            break;
        }

        case "com.plysystems.nextxlr.micmute": {
            const mode = settings.mode || "toggle";
            if (mode === "hold") {
                await callEngineApi("/api/mic-mute", { muted: false });
                await refreshEngineState();
            }
            break;
        }
    }
}

async function handleDialRotate(context, action, settings, ticks) {
    if (action === "com.plysystems.nextxlr.fader" || action === "com.plysystems.nextxlr.encoder") {
        const channel = settings.channel || "music";
        const delta = (ticks * 0.02);
        await callEngineApi("/api/fader", { channel, delta });
        await refreshEngineState();
    }
}

async function handleDialPress(context, action, settings) {
    if (action === "com.plysystems.nextxlr.fader" || action === "com.plysystems.nextxlr.encoder") {
        const channel = settings.channel || "music";
        await callEngineApi("/api/mute", { channel, action: "toggle" });
        await refreshEngineState();
    }
}

async function handleTouchTap(context, action, settings) {
    if (action === "com.plysystems.nextxlr.fader" || action === "com.plysystems.nextxlr.encoder") {
        const channel = settings.channel || "music";
        await callEngineApi("/api/fader", { channel, volume: 1.0 });
        await refreshEngineState();
    }
}

// --- UI Display Updates ---

function updateAllActiveContexts() {
    activeContexts.forEach((info, context) => {
        updateContextDisplay(context, info.action, info.settings);
    });
}

function updateContextDisplay(context, action, settings) {
    if (!lastMixerState) {
        if (!isEngineOnline) {
            setTitle(context, "NEXT:XLR\nOffline");
        }
        return;
    }

    const channelName = settings.channel || "music";
    const channelObj = findChannelInState(channelName);

    switch (action) {
        case "com.plysystems.nextxlr.encoder":
        case "com.plysystems.nextxlr.fader": {
            if (channelObj) {
                const volPct = Math.round(channelObj.volume * 100);
                const title = channelObj.muted ? `${channelObj.name}\nMUTE` : `${channelObj.name}\n${volPct}%`;
                setTitle(context, title);

                // Stream Deck + Encoder feedback
                setFeedback(context, {
                    title: channelObj.name,
                    value: channelObj.muted ? "MUTE" : `${volPct}%`,
                    indicator: volPct,
                    icon: channelObj.muted ? "assets/muteOn.png" : "assets/faderAction.png"
                });
            }
            break;
        }

        case "com.plysystems.nextxlr.mute": {
            if (channelObj) {
                setState(context, channelObj.muted ? 1 : 0);
                setTitle(context, `${channelObj.name}\n${channelObj.muted ? "MUTED" : "ON"}`);
            }
            break;
        }

        case "com.plysystems.nextxlr.fadermute": {
            const faderLetter = (settings.fader || "A").toUpperCase();
            const ch = findChannelInState(faderLetter);
            if (ch) {
                setState(context, ch.muted ? 1 : 0);
                setTitle(context, `FADER ${faderLetter}\n${ch.name}\n${ch.muted ? "MUTED" : "ON"}`);
            } else {
                setTitle(context, `FADER ${faderLetter}\n--`);
            }
            break;
        }

        case "com.plysystems.nextxlr.page": {
            const curPage = (lastMixerState && lastMixerState.active_page) || 1;
            setState(context, curPage === 2 ? 1 : 0);
            setTitle(context, `MIXER\nSEITE ${curPage}`);
            break;
        }

        case "com.plysystems.nextxlr.mixmode": {
            const isSubmix = (lastMixerState && lastMixerState.fader_target_submix !== undefined) ? lastMixerState.fader_target_submix : true;
            setState(context, isSubmix ? 0 : 1);
            setTitle(context, `MIX\n${isSubmix ? "KOPFHÖRER" : "STREAM"}`);
            break;
        }

        case "com.plysystems.nextxlr.micmute": {
            const isMuted = (lastMixerState.mic_muted !== undefined)
                ? lastMixerState.mic_muted
                : (findChannelInState("mic") ? findChannelInState("mic").muted : false);
            setState(context, isMuted ? 1 : 0);
            setTitle(context, isMuted ? "MIC\nMUTED" : "MIC\nLIVE");
            break;
        }

        case "com.plysystems.nextxlr.bleep": {
            const isBleep = (lastMixerState.bleep !== undefined) ? lastMixerState.bleep : (lastMixerState.bleep_active || false);
            setState(context, isBleep ? 1 : 0);
            setTitle(context, isBleep ? "BLEEP\nAKTIV" : "ZENSUR\nBLEEP");
            break;
        }

        case "com.plysystems.nextxlr.profile": {
            const prof = settings.profileName || "Gaming";
            setTitle(context, `Profil:\n${prof}`);
            break;
        }

        case "com.plysystems.nextxlr.route": {
            const bus = settings.bus || "stream";
            const routed = isRouteActive(channelName, bus);
            setState(context, routed ? 1 : 0);
            setTitle(context, `${channelName}\n→ ${bus}`);
            break;
        }
    }
}

function findChannelInState(name) {
    if (!lastMixerState || !lastMixerState.channels) return null;
    const clean = name.toLowerCase().trim();
    // Check faders A, B, C, D
    if (clean.length === 1 && ["a", "b", "c", "d"].includes(clean)) {
        const fa = lastMixerState.faders.find(f => f.fader.toLowerCase() === clean);
        if (fa) return lastMixerState.channels.find(c => c.id === fa.channel);
    }
    return lastMixerState.channels.find(c => c.name.toLowerCase().includes(clean) || clean.includes(c.name.toLowerCase()));
}

function isRouteActive(chName, busName) {
    if (!lastMixerState || !lastMixerState.routes) return false;
    const ch = findChannelInState(chName);
    if (!ch) return false;
    const busClean = busName.toLowerCase();
    const bus = lastMixerState.buses ? lastMixerState.buses.find(b => b.name.toLowerCase().includes(busClean)) : null;
    if (!bus) return false;
    const route = lastMixerState.routes.find(r => r.channel === ch.id && r.bus === bus.id);
    return route ? route.gain > 0 : false;
}

