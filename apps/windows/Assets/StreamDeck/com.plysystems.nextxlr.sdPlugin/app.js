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
        if (res.external_speaker_enabled !== undefined) {
            lastMixerState.external_speaker_enabled = res.external_speaker_enabled;
        }
        if (res.wireless_routing !== undefined) {
            lastMixerState.wireless_routing = res.wireless_routing;
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

function setImage(context, image) {
    sendToStreamDeck("setImage", context, { payload: { image, target: 0 } });
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

// In-memory Canvas renderer for beautiful dynamic studio Scribble keys
function renderCanvasFader(channelName, volPct, isMuted, isSubmix) {
    try {
        let canvas = document.getElementById("renderCanvas");
        if (!canvas) {
            canvas = document.createElement("canvas");
            canvas.id = "renderCanvas";
            canvas.width = 144;
            canvas.height = 144;
            document.body.appendChild(canvas);
        }
        const ctx = canvas.getContext("2d");
        ctx.clearRect(0, 0, 144, 144);

        // Sleek dark panel with studio console aesthetic
        ctx.fillStyle = "#0E1015";
        ctx.beginPath();
        ctx.roundRect(4, 4, 136, 136, 18);
        ctx.fill();

        // High-contrast accent border
        ctx.strokeStyle = isMuted ? "#EF4444" : (isSubmix ? "#3B82F6" : "#00D2B4");
        ctx.lineWidth = 4;
        ctx.stroke();

        // Channel title
        ctx.fillStyle = "#94A3B8";
        ctx.font = "bold 16px sans-serif";
        ctx.textAlign = "center";
        const shortName = channelName.length > 9 ? channelName.slice(0, 8) + ".." : channelName;
        ctx.fillText(shortName.toUpperCase(), 72, 34);

        if (isSubmix) {
            ctx.fillStyle = "#3B82F6";
            ctx.font = "bold 11px sans-serif";
            ctx.fillText("PHONES", 72, 50);
        }

        // Center Level / Mute
        if (isMuted) {
            // Crossed-out microphone icon
            const cx = 72, cy = 64;
            ctx.save();
            // Capsule
            ctx.fillStyle = "#EF4444";
            ctx.beginPath();
            ctx.roundRect(cx - 8, cy - 20, 16, 26, 8);
            ctx.fill();

            // U-arc
            ctx.strokeStyle = "#EF4444";
            ctx.lineWidth = 3;
            ctx.beginPath();
            ctx.arc(cx, cy - 6, 13, 0, Math.PI);
            ctx.stroke();

            // Stem and base
            ctx.beginPath();
            ctx.moveTo(cx, cy + 7);
            ctx.lineTo(cx, cy + 17);
            ctx.moveTo(cx - 10, cy + 17);
            ctx.lineTo(cx + 10, cy + 17);
            ctx.stroke();

            // Bold white/red diagonal slash through the mic
            ctx.strokeStyle = "#EF4444";
            ctx.lineWidth = 4;
            ctx.beginPath();
            ctx.moveTo(cx - 18, cy - 22);
            ctx.lineTo(cx + 18, cy + 20);
            ctx.stroke();

            ctx.strokeStyle = "#FFFFFF";
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(cx - 17, cy - 21);
            ctx.lineTo(cx + 17, cy + 19);
            ctx.stroke();
            ctx.restore();

            // MUTE text
            ctx.fillStyle = "#EF4444";
            ctx.font = "900 18px sans-serif";
            ctx.fillText("MUTE", 72, 102);

            // Red mute bar
            ctx.fillStyle = "rgba(239, 68, 68, 0.35)";
            ctx.beginPath();
            ctx.roundRect(18, 116, 108, 12, 6);
            ctx.fill();
        } else {
            // Unmuted: 0 - 100 level display
            ctx.fillStyle = "#FFFFFF";
            ctx.font = "900 32px sans-serif";
            ctx.fillText(`${volPct}`, 72, isSubmix ? 82 : 76);

            // Progress bar background
            ctx.fillStyle = "#1E293B";
            ctx.beginPath();
            ctx.roundRect(18, 106, 108, 16, 8);
            ctx.fill();

            // Active fill
            const fillW = Math.max(8, Math.round((Math.min(volPct, 100) / 100) * 108));
            ctx.fillStyle = isSubmix ? "#3B82F6" : "#00D2B4";
            ctx.beginPath();
            ctx.roundRect(18, 106, fillW, 16, 8);
            ctx.fill();
        }

        return canvas.toDataURL("image/png");
    } catch (e) {
        return null;
    }
}

function renderCanvasPage(pageNum) {
    try {
        let canvas = document.getElementById("renderCanvasPage");
        if (!canvas) {
            canvas = document.createElement("canvas");
            canvas.id = "renderCanvasPage";
            canvas.width = 144;
            canvas.height = 144;
            document.body.appendChild(canvas);
        }
        const ctx = canvas.getContext("2d");
        ctx.clearRect(0, 0, 144, 144);

        const isP2 = (pageNum === 2);
        const accentColor = isP2 ? "#F59E0B" : "#00D2B4"; // Orange for Seite 2, Cyan for Seite 1
        const badgeBg = isP2 ? "rgba(245, 158, 11, 0.18)" : "rgba(0, 210, 180, 0.18)";

        // Dark background
        ctx.fillStyle = "#0E1015";
        ctx.beginPath();
        ctx.roundRect(4, 4, 136, 136, 18);
        ctx.fill();

        // Accent border
        ctx.strokeStyle = accentColor;
        ctx.lineWidth = 4;
        ctx.stroke();

        // Header: "SEITE"
        ctx.fillStyle = "#94A3B8";
        ctx.font = "bold 15px sans-serif";
        ctx.textAlign = "center";
        ctx.fillText("SEITE", 72, 32);

        // Center circular badge
        ctx.fillStyle = badgeBg;
        ctx.beginPath();
        ctx.arc(72, 74, 32, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = accentColor;
        ctx.lineWidth = 3;
        ctx.stroke();

        // Big Page number "1" or "2"
        ctx.fillStyle = accentColor;
        ctx.font = "900 42px sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(pageNum.toString(), 72, 75);
        ctx.textBaseline = "alphabetic";

        // Footer: "BANK 1" or "BANK 2"
        ctx.fillStyle = accentColor;
        ctx.font = "bold 13px sans-serif";
        ctx.fillText(isP2 ? "BANK 2" : "BANK 1", 72, 126);

        return canvas.toDataURL("image/png");
    } catch (e) {
        return null;
    }
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
            const mix = settings.mix || "auto";
            let res;
            if (mode === "increase") {
                res = await callEngineApi("/api/fader", { channel, delta: step, mix });
            } else if (mode === "decrease") {
                res = await callEngineApi("/api/fader", { channel, delta: -step, mix });
            } else if (mode === "set") {
                const vol = parseFloat(settings.volume || "0.75");
                res = await callEngineApi("/api/fader", { channel, volume: vol, mix });
            } else if (mode === "toggle_mute") {
                res = await callEngineApi("/api/mute", { channel, action: "toggle", mix });
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
            const profileType = settings.profileType || "nextxlr";
            const profileName = settings.profileName || (profileType === "mic" ? "Standard" : "Gaming");
            const res = await callEngineApi("/api/profile", { name: profileName, category: profileType });
            if (res && res.ok) showOk(context); else showAlert(context);
            await refreshEngineState();
            break;
        }

        case "com.plysystems.nextxlr.route": {
            const bus = settings.bus || "stream";
            const normBus = normalizeBusToken(bus);
            if (normBus === "external" && lastMixerState && !lastMixerState.external_speaker_enabled) {
                showAlert(context);
                return;
            }
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
        const mix = settings.mix || "auto";
        await callEngineApi("/api/fader", { channel, delta, mix });
        await refreshEngineState();
    }
}

async function handleDialPress(context, action, settings) {
    if (action === "com.plysystems.nextxlr.fader" || action === "com.plysystems.nextxlr.encoder") {
        const channel = settings.channel || "music";
        const mix = settings.mix || "auto";
        await callEngineApi("/api/mute", { channel, action: "toggle", mix });
        await refreshEngineState();
    }
}

async function handleTouchTap(context, action, settings) {
    if (action === "com.plysystems.nextxlr.fader" || action === "com.plysystems.nextxlr.encoder") {
        const channel = settings.channel || "music";
        const mix = settings.mix || "auto";
        await callEngineApi("/api/fader", { channel, volume: 1.0, mix });
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
            const mode = settings.mode || "display";
            if (mode === "increase") {
                setImage(context, "assets/faderUp.png");
                const stepPct = Math.round(parseFloat(settings.step || "0.05") * 100);
                const titleName = channelObj ? channelObj.name : channelName;
                setTitle(context, `${titleName}\n+${stepPct}%`);
            } else if (mode === "decrease") {
                setImage(context, "assets/faderDown.png");
                const stepPct = Math.round(parseFloat(settings.step || "0.05") * 100);
                const titleName = channelObj ? channelObj.name : channelName;
                setTitle(context, `${titleName}\n-${stepPct}%`);
            } else if (channelObj) {
                const targetSubmix = settings.mix === "headphones" || (settings.mix !== "stream" && (lastMixerState && lastMixerState.fader_target_submix));
                const volPct = Math.round((targetSubmix ? channelObj.submix_volume : channelObj.volume) * 100);
                const isMuted = targetSubmix ? channelObj.submix_muted : channelObj.muted;

                // Render dynamic high-res console Scribble key
                const dataUrl = renderCanvasFader(channelObj.name, volPct, isMuted, targetSubmix);
                if (dataUrl) {
                    setImage(context, dataUrl);
                    setTitle(context, "");
                } else {
                    const title = isMuted ? `${channelObj.name}\nMUTE` : `${channelObj.name}\n${volPct}%`;
                    setTitle(context, title);
                }

                // Stream Deck + Encoder feedback
                setFeedback(context, {
                    title: channelObj.name,
                    value: isMuted ? "MUTE" : `${volPct}%`,
                    indicator: {
                        value: volPct,
                        enabled: true,
                        bar_fill_c: isMuted ? "#EF4444" : (targetSubmix ? "#3B82F6" : "#00D2B4")
                    },
                    icon: isMuted ? "assets/muteOn.png" : "assets/faderAction.png"
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

        case "com.plysystems.nextxlr.page": {
            const curPage = (lastMixerState && lastMixerState.active_page) || 1;
            setState(context, curPage === 2 ? 1 : 0);
            const dataUrl = renderCanvasPage(curPage);
            if (dataUrl) {
                setImage(context, dataUrl);
                setTitle(context, "");
            } else {
                setTitle(context, `SEITE\n${curPage}`);
            }
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
            const pType = settings.profileType || "nextxlr";
            const prof = settings.profileName || (pType === "mic" ? "Standard" : "Gaming");
            const header = (pType === "mic") ? "MIC" : "PROFIL";
            setTitle(context, `${header}\n${prof}`);
            break;
        }

        case "com.plysystems.nextxlr.route": {
            const bus = settings.bus || "stream";
            const normBus = normalizeBusToken(bus);
            const titleName = channelObj ? channelObj.name : channelName;
            if (normBus === "external") {
                if (lastMixerState && !lastMixerState.external_speaker_enabled) {
                    setImage(context, "assets/speakerDisabled.png");
                    setTitle(context, `${titleName}\nDeaktiviert`);
                } else {
                    const routed = isRouteActive(channelName, "external");
                    setImage(context, routed ? "assets/speakerOn.png" : "assets/speakerOff.png");
                    setTitle(context, `${titleName}\n${routed ? "ON" : "OFF"}`);
                }
            } else {
                const routed = isRouteActive(channelName, bus);
                setState(context, routed ? 1 : 0);
                setTitle(context, `${titleName}\n→ ${bus}`);
            }
            break;
        }
    }
}

function normalizeChannelToken(str) {
    if (!str) return "";
    const clean = str.toString().toLowerCase().replace(/[\s_\-]+/g, "");
    if (["mic", "mikrofon", "microphone"].includes(clean)) return "mic";
    if (["chat", "discord"].includes(clean)) return "chat";
    if (["music", "musik", "spotify"].includes(clean)) return "music";
    if (["game", "spiel"].includes(clean)) return "game";
    if (["system"].includes(clean)) return "system";
    if (["samples", "sampler"].includes(clean)) return "samples";
    if (["linein", "line"].includes(clean)) return "linein";
    if (["console", "konsole"].includes(clean)) return "console";
    if (["headphones", "kopfhörer", "kopfhoerer", "hp"].includes(clean)) return "headphones";
    if (["monitor"].includes(clean)) return "monitor";
    if (["lineout"].includes(clean)) return "lineout";
    if (["stream", "broadcast", "broadcaststreammix"].includes(clean)) return "stream";
    if (["chatmic"].includes(clean)) return "chatmic";
    return clean;
}

function normalizeBusToken(str) {
    if (!str) return "";
    const clean = str.toString().toLowerCase().replace(/[\s_\-]+/g, "");
    if (["headphones", "kopfhörer", "kopfhoerer", "hp"].includes(clean)) return "headphones";
    if (["stream", "broadcast", "broadcaststreammix"].includes(clean)) return "stream";
    if (["lineout"].includes(clean)) return "lineout";
    if (["chatmic", "chat"].includes(clean)) return "chatmic";
    if (["sampler", "samples"].includes(clean)) return "sampler";
    if (["external", "extern", "speaker", "lautsprecher", "wireless", "bluetooth"].includes(clean)) return "external";
    return clean;
}

function findChannelInState(name) {
    if (!lastMixerState || !lastMixerState.channels) return null;
    const clean = (name || "").toString().toLowerCase().trim();
    if (!clean) return null;

    // Check faders A, B, C, D
    if (clean.length === 1 && ["a", "b", "c", "d"].includes(clean)) {
        const fa = lastMixerState.faders && lastMixerState.faders.find(f => f.fader.toLowerCase() === clean);
        if (fa) return lastMixerState.channels.find(c => c.id === fa.channel);
    }

    const normInput = normalizeChannelToken(clean);
    // 1. Exact token match
    let match = lastMixerState.channels.find(c => normalizeChannelToken(c.name) === normInput);
    if (match) return match;

    // 2. Substring match
    const strippedClean = clean.replace(/[\s_\-]+/g, "");
    match = lastMixerState.channels.find(c => {
        const cClean = c.name.toLowerCase().replace(/[\s_\-]+/g, "");
        return cClean.includes(strippedClean) || strippedClean.includes(cClean);
    });
    return match || null;
}

function isRouteActive(chName, busName) {
    if (!lastMixerState) return false;
    const normBus = normalizeBusToken(busName);
    if (normBus === "external") {
        if (!lastMixerState.external_speaker_enabled) return false;
        const ch = findChannelInState(chName);
        if (!ch) return false;
        if (lastMixerState.wireless_routing) {
            for (const [k, v] of Object.entries(lastMixerState.wireless_routing)) {
                if (normalizeChannelToken(k) === normalizeChannelToken(ch.name)) {
                    return !!v;
                }
            }
        }
        return false;
    }

    if (!lastMixerState.routes) return false;
    const ch = findChannelInState(chName);
    if (!ch) return false;
    const bus = lastMixerState.buses ? lastMixerState.buses.find(b => normalizeBusToken(b.name) === normBus) : null;
    if (!bus) return false;
    const route = lastMixerState.routes.find(r => r.channel === ch.id && r.bus === bus.id);
    return route ? route.gain > 0 : false;
}

