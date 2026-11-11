let websocket = null;
let uuid = null;
let actionInfo = {};
let settings = {};

function connectElgatoStreamDeckSocket(inPort, inUUID, inRegisterEvent, inInfo, inActionInfo) {
    uuid = inUUID;
    actionInfo = JSON.parse(inActionInfo);
    settings = actionInfo.payload.settings || {};

    websocket = new WebSocket("ws://127.0.0.1:" + inPort);

    websocket.onopen = function () {
        const registerJson = {
            event: inRegisterEvent,
            uuid: inUUID
        };
        websocket.send(JSON.stringify(registerJson));

        // Initialize UI with current settings
        initUI();
    };

    websocket.onmessage = function (evt) {
        try {
            const data = JSON.parse(evt.data);
            if (data.event === "didReceiveSettings") {
                settings = data.payload.settings;
                initUI();
            }
        } catch (e) {
            console.error(e);
        }
    };
}

function saveSettings() {
    if (websocket && websocket.readyState === WebSocket.OPEN) {
        const json = {
            event: "setSettings",
            context: uuid,
            payload: settings
        };
        websocket.send(JSON.stringify(json));
    }
}

function sendToPlugin(payload) {
    if (websocket && websocket.readyState === WebSocket.OPEN) {
        const json = {
            event: "sendToPlugin",
            context: uuid,
            payload: payload
        };
        websocket.send(JSON.stringify(json));
    }
}

