const viewer = document.getElementById("arViewer");
const cameraButton = document.getElementById("cameraButton");
const arButton = document.getElementById("arButton");
const updateButton = document.getElementById("updateButton");
const voiceButton = document.getElementById("voiceButton");
const loadingOverlay = document.getElementById("loadingOverlay");
const gestureHint = document.getElementById("gestureHint");
const cameraFeed = document.getElementById("cameraFeed");

const materialValue = document.getElementById("materialValue");
const massValue = document.getElementById("massValue");
const stressValue = document.getElementById("stressValue");
const statusText = document.getElementById("statusText");

const fallbackModel = "https://modelviewer.dev/shared-assets/models/Astronaut.glb";
const API_PORT_CANDIDATES = [3000, 3001, 3002, 3003, 3004, 3005, 3010];

let currentData = {
  modelUrl: "/models/gear.glb",
  material: "Steel",
  mass: "2.3 kg",
  stress: "380 MPa"
};

let cameraStream = null;
let apiBaseUrl = window.location.origin;

function setStatus(text, variant = "") {
  statusText.textContent = text;
  statusText.className = "status";
  if (variant) {
    statusText.classList.add(variant);
  }
}

function showLoader(active) {
  loadingOverlay.classList.toggle("active", active);
}

function updateInfoPanel(data) {
  materialValue.textContent = data.material;
  massValue.textContent = data.mass;
  stressValue.textContent = data.stress;
}

function buildCandidateOrigins() {
  const origins = [window.location.origin];
  const host = window.location.hostname;

  if (host) {
    for (const port of API_PORT_CANDIDATES) {
      origins.push(`${window.location.protocol}//${host}:${port}`);
    }
  }

  return Array.from(new Set(origins));
}

async function pingHealth(origin) {
  try {
    const response = await fetch(`${origin}/health`, {
      method: "GET",
      mode: "cors",
      cache: "no-store"
    });

    return response.ok;
  } catch {
    return false;
  }
}

async function discoverApiBase() {
  const candidates = buildCandidateOrigins();

  for (const origin of candidates) {
    // eslint-disable-next-line no-await-in-loop
    const ok = await pingHealth(origin);
    if (ok) {
      apiBaseUrl = origin;
      return origin;
    }
  }

  return null;
}

async function fetchModelData(allowRetry = true) {
  try {
    setStatus("Fetching latest model data...", "warn");
    showLoader(true);

    const response = await fetch(`${apiBaseUrl}/model-data`, {
      method: "GET",
      mode: "cors",
      cache: "no-store"
    });

    if (!response.ok) {
      throw new Error(`Server responded ${response.status}`);
    }

    const payload = await response.json();
    currentData = payload;

    const srcCandidate = payload.modelUrl || fallbackModel;
    viewer.src = srcCandidate;

    updateInfoPanel(payload);
    setStatus(`Model and engineering data updated (${apiBaseUrl}).`, "ok");
  } catch (error) {
    console.error(error);

    if (allowRetry) {
      const discovered = await discoverApiBase();
      if (discovered) {
        setStatus(`Connected to API at ${discovered}. Retrying model fetch...`, "warn");
        await fetchModelData(false);
        return;
      }
    }

    if (!viewer.src) {
      viewer.src = fallbackModel;
    }

    updateInfoPanel(currentData);
    setStatus("Could not find backend API. Start server and open the exact URL shown in terminal (same host/port).", "error");
  } finally {
    showLoader(false);
  }
}

function isSecureArContext() {
  return window.isSecureContext || window.location.hostname === "localhost" || window.location.hostname === "127.0.0.1";
}

async function toggleCamera() {
  if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
    setStatus("Camera API is unavailable in this browser.", "error");
    return;
  }

  if (cameraStream) {
    cameraStream.getTracks().forEach((track) => track.stop());
    cameraStream = null;
    cameraFeed.srcObject = null;
    cameraFeed.classList.remove("active");
    cameraButton.textContent = "Open Camera";
    setStatus("Camera stopped.", "warn");
    return;
  }

  try {
    const stream = await navigator.mediaDevices.getUserMedia({
      video: { facingMode: { ideal: "environment" } },
      audio: false
    });

    cameraStream = stream;
    cameraFeed.srcObject = stream;
    cameraFeed.classList.add("active");
    cameraButton.textContent = "Stop Camera";
    setStatus("Camera opened. You can inspect model over live feed.", "ok");
  } catch (error) {
    console.error(error);
    setStatus("Camera permission denied or unavailable. Allow camera access and retry.", "error");
  }
}

function triggerAr() {
  if (!isSecureArContext()) {
    setStatus("AR requires HTTPS (or localhost). Open this app in a secure context.", "error");
    return;
  }

  if (typeof viewer.activateAR !== "function") {
    setStatus("AR launcher is unavailable in this browser.", "error");
    return;
  }

  if (!viewer.canActivateAR) {
    setStatus("Real-world AR is supported on mobile AR browsers (Android Chrome or iOS Safari). Use Open Camera for desktop preview.", "warn");
    return;
  }

  viewer.activateAR();
  setStatus("Opening AR mode...", "ok");
}

function toggleGestureHint() {
  gestureHint.style.display = gestureHint.style.display === "none" ? "grid" : "none";
}

function handleModelTap() {
  updateInfoPanel(currentData);
  setStatus("Engineering panel refreshed from model tap.", "ok");
}

function setupVoiceCommands() {
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRecognition) {
    setStatus("Voice commands unavailable in this browser.", "warn");
    voiceButton.disabled = true;
    return;
  }

  const recognition = new SpeechRecognition();
  recognition.lang = "en-US";
  recognition.interimResults = false;
  recognition.maxAlternatives = 1;

  recognition.onresult = (event) => {
    const transcript = event.results[0][0].transcript.toLowerCase();
    setStatus(`Voice command: ${transcript}`, "ok");

    if (transcript.includes("update")) {
      fetchModelData();
      return;
    }

    if (transcript.includes("real world") || transcript.includes("ar")) {
      triggerAr();
      return;
    }

    if (transcript.includes("hint")) {
      toggleGestureHint();
      return;
    }

    if (transcript.includes("details") || transcript.includes("panel")) {
      handleModelTap();
      return;
    }

    setStatus("Command not recognized. Try: update, real world, details.", "warn");
  };

  recognition.onerror = () => {
    setStatus("Voice command error. Please try again.", "error");
  };

  voiceButton.addEventListener("click", () => {
    setStatus("Listening for command...", "warn");
    recognition.start();
  });
}

cameraButton.addEventListener("click", toggleCamera);
arButton.addEventListener("click", triggerAr);
updateButton.addEventListener("click", fetchModelData);
viewer.addEventListener("click", handleModelTap);
viewer.addEventListener("load", () => showLoader(false));
viewer.addEventListener("error", () => {
  viewer.src = fallbackModel;
  setStatus("Model file failed to load. Switched to fallback model asset.", "error");
  showLoader(false);
});
viewer.addEventListener("ar-status", (event) => {
  const status = event?.detail?.status;
  if (status === "session-started") {
    setStatus("AR session started.", "ok");
    return;
  }

  if (status === "failed") {
    setStatus("AR launch failed on this device/browser. Use Open Camera fallback or test on supported mobile AR device.", "error");
    return;
  }

  if (status === "not-presenting") {
    setStatus("AR session ended.", "warn");
  }
});

setupVoiceCommands();

discoverApiBase()
  .then((origin) => {
    if (origin) {
      setStatus(`Connected to backend: ${origin}`, "ok");
    } else {
      setStatus("Backend not auto-detected yet. Start server and tap Update Model.", "warn");
    }

    fetchModelData();
  })
  .catch(() => {
    fetchModelData();
  });

setInterval(() => {
  fetchModelData();
}, 30000);

window.addEventListener("beforeunload", () => {
  if (cameraStream) {
    cameraStream.getTracks().forEach((track) => track.stop());
  }
});
