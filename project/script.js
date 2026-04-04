const threeCanvas = document.getElementById("threeCanvas");
const inputVideo = document.getElementById("inputVideo");
const handCanvas = document.getElementById("handCanvas");
const handCtx = handCanvas.getContext("2d");

const trackingBadge = document.getElementById("trackingBadge");
const gestureBadge = document.getElementById("gestureBadge");
const cameraStatus = document.getElementById("cameraStatus");
const infoPanel = document.getElementById("infoPanel");
const loadingOverlay = document.getElementById("loadingOverlay");
const stageShell = document.getElementById("stageShell");

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x02050b);
scene.fog = new THREE.Fog(0x02050b, 8, 22);

const camera = new THREE.PerspectiveCamera(52, 1, 0.1, 100);
camera.position.set(0, 1.2, 4.4);

const renderer = new THREE.WebGLRenderer({
  canvas: threeCanvas,
  antialias: true,
  alpha: true
});
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
if ("outputColorSpace" in renderer) {
  renderer.outputColorSpace = THREE.SRGBColorSpace;
}

const ambient = new THREE.AmbientLight(0x7ec9ff, 0.45);
scene.add(ambient);

const keyLight = new THREE.DirectionalLight(0x9be3ff, 1.2);
keyLight.position.set(2.5, 4.8, 2.2);
keyLight.castShadow = true;
keyLight.shadow.mapSize.set(1024, 1024);
scene.add(keyLight);

const rimLight = new THREE.DirectionalLight(0x2f8dff, 0.45);
rimLight.position.set(-3.2, 1.5, -2.8);
scene.add(rimLight);

const ground = new THREE.Mesh(
  new THREE.CircleGeometry(8, 96),
  new THREE.MeshStandardMaterial({
    color: 0x09172a,
    metalness: 0.2,
    roughness: 0.8,
    transparent: true,
    opacity: 0.75
  })
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = -1.35;
ground.receiveShadow = true;
scene.add(ground);

const modelAnchor = new THREE.Group();
scene.add(modelAnchor);

let modelRoot = null;
const targetState = {
  x: 0,
  y: 0,
  scale: 1,
  rotationY: 0
};

const gestureState = {
  previousPinch: null,
  previousWristX: null,
  lastSwipeAt: 0,
  openPalmFrames: 0,
  selected: false
};

const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
let animationStarted = false;

function setLoading(active, text = "Booting hologram...") {
  loadingOverlay.classList.toggle("active", active);
  const label = loadingOverlay.querySelector("span");
  if (label) {
    label.textContent = text;
  }
}

function setTrackingStatus(active) {
  trackingBadge.textContent = `Tracking: ${active ? "ACTIVE" : "LOST"}`;
  trackingBadge.classList.remove("ok", "warn", "error");
  trackingBadge.classList.add(active ? "ok" : "error");
}

function setGestureStatus(label) {
  gestureBadge.textContent = `Gesture: ${label}`;
}

function setCameraStatus(text, variant = "") {
  cameraStatus.textContent = `Camera: ${text}`;
  cameraStatus.classList.remove("ok", "warn", "error");
  if (variant) {
    cameraStatus.classList.add(variant);
  }
}

function revealInfoPanel() {
  gestureState.selected = true;
  infoPanel.classList.remove("hidden");
}

function onCanvasPick(event) {
  if (!modelRoot) {
    return;
  }

  const rect = threeCanvas.getBoundingClientRect();
  pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

  raycaster.setFromCamera(pointer, camera);
  const hits = raycaster.intersectObject(modelRoot, true);
  if (hits.length > 0) {
    revealInfoPanel();
    setGestureStatus("SELECT");
  }
}

threeCanvas.addEventListener("pointerdown", onCanvasPick);

function createFallbackModel() {
  const fallback = new THREE.Mesh(
    new THREE.TorusKnotGeometry(0.8, 0.22, 200, 28),
    new THREE.MeshStandardMaterial({ color: 0x57d6ff, metalness: 0.6, roughness: 0.3 })
  );
  fallback.castShadow = true;
  fallback.receiveShadow = true;
  return fallback;
}

function loadModel() {
  return new Promise((resolve) => {
    let completed = false;
    const completeWith = (root) => {
      if (completed) {
        return;
      }

      completed = true;
      modelAnchor.add(root);
      modelRoot = root;
      resolve();
    };

    const useFallback = () => {
      completeWith(createFallbackModel());
    };

    if (!THREE || typeof THREE.GLTFLoader !== "function") {
      useFallback();
      return;
    }

    const timeout = setTimeout(() => {
      useFallback();
    }, 7000);

    try {
      const loader = new THREE.GLTFLoader();
      loader.load(
        "/models/gear.glb",
        (gltf) => {
          clearTimeout(timeout);

          const root = gltf.scene;
          root.traverse((node) => {
            if (node.isMesh) {
              node.castShadow = true;
              node.receiveShadow = true;
            }
          });

          const box = new THREE.Box3().setFromObject(root);
          const size = new THREE.Vector3();
          box.getSize(size);
          const center = new THREE.Vector3();
          box.getCenter(center);

          root.position.sub(center);
          const maxDim = Math.max(size.x, size.y, size.z) || 1;
          const normalizedScale = 2.2 / maxDim;
          root.scale.setScalar(normalizedScale);

          completeWith(root);
        },
        undefined,
        () => {
          clearTimeout(timeout);
          useFallback();
        }
      );
    } catch (error) {
      clearTimeout(timeout);
      console.error(error);
      useFallback();
    }
  });
}

function resizeRenderer() {
  const width = stageShell.clientWidth;
  const height = stageShell.clientHeight;
  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

window.addEventListener("resize", resizeRenderer);
resizeRenderer();

function distance2D(a, b) {
  const dx = a.x - b.x;
  const dy = a.y - b.y;
  return Math.sqrt(dx * dx + dy * dy);
}

function isOpenPalm(landmarks) {
  const conditions = [
    landmarks[8].y < landmarks[6].y,
    landmarks[12].y < landmarks[10].y,
    landmarks[16].y < landmarks[14].y,
    landmarks[20].y < landmarks[18].y
  ];

  return conditions.every(Boolean);
}

function mapHandToScene(centerX, centerY) {
  // Convert normalized 0..1 camera coordinates into scene space.
  targetState.x = (0.5 - centerX) * 4.2;
  targetState.y = (0.52 - centerY) * 2.8;
}

function onHandResults(results) {
  handCanvas.width = results.image.width;
  handCanvas.height = results.image.height;

  handCtx.save();
  handCtx.clearRect(0, 0, handCanvas.width, handCanvas.height);
  handCtx.drawImage(results.image, 0, 0, handCanvas.width, handCanvas.height);

  if (!results.multiHandLandmarks || results.multiHandLandmarks.length === 0) {
    setTrackingStatus(false);
    setGestureStatus("IDLE");
    gestureState.previousPinch = null;
    gestureState.openPalmFrames = 0;
    handCtx.restore();
    return;
  }

  const landmarks = results.multiHandLandmarks[0];
  setTrackingStatus(true);

  if (
    typeof drawConnectors === "function" &&
    typeof drawLandmarks === "function" &&
    typeof HAND_CONNECTIONS !== "undefined"
  ) {
    drawConnectors(handCtx, landmarks, HAND_CONNECTIONS, {
      color: "#30d4ff",
      lineWidth: 3
    });

    drawLandmarks(handCtx, landmarks, {
      color: "#0f1122",
      fillColor: "#8ce9ff",
      lineWidth: 1,
      radius: 3
    });
  }

  const thumbTip = landmarks[4];
  const indexTip = landmarks[8];
  const wrist = landmarks[0];

  const centerX = (landmarks[0].x + landmarks[5].x + landmarks[9].x + landmarks[13].x + landmarks[17].x) / 5;
  const centerY = (landmarks[0].y + landmarks[5].y + landmarks[9].y + landmarks[13].y + landmarks[17].y) / 5;
  mapHandToScene(centerX, centerY);

  let gestureLabel = "MOVE";

  const pinchDistance = distance2D(thumbTip, indexTip);
  const pinchActive = pinchDistance < 0.055;

  if (pinchActive) {
    if (gestureState.previousPinch !== null) {
      const pinchDelta = pinchDistance - gestureState.previousPinch;
      targetState.scale = THREE.MathUtils.clamp(targetState.scale + pinchDelta * 9.5, 0.45, 2.8);
    }

    gestureState.previousPinch = pinchDistance;
    gestureLabel = "PINCH";
  } else {
    gestureState.previousPinch = null;
  }

  const now = performance.now();
  if (gestureState.previousWristX !== null) {
    const wristDelta = wrist.x - gestureState.previousWristX;
    if (Math.abs(wristDelta) > 0.06 && now - gestureState.lastSwipeAt > 260) {
      targetState.rotationY += wristDelta > 0 ? -0.48 : 0.48;
      gestureState.lastSwipeAt = now;
      gestureLabel = wristDelta > 0 ? "SWIPE RIGHT" : "SWIPE LEFT";
    }
  }

  gestureState.previousWristX = wrist.x;

  if (isOpenPalm(landmarks) && pinchDistance > 0.07) {
    gestureState.openPalmFrames += 1;
    if (gestureState.openPalmFrames > 6) {
      revealInfoPanel();
      gestureLabel = "OPEN PALM";
    }
  } else {
    gestureState.openPalmFrames = 0;
  }

  setGestureStatus(gestureLabel);
  handCtx.restore();
}

async function startHandTracking() {
  const secureContext =
    window.isSecureContext ||
    window.location.hostname === "localhost" ||
    window.location.hostname === "127.0.0.1";

  if (!secureContext) {
    setCameraStatus("BLOCKED (open via HTTPS or localhost)", "error");
    return;
  }

  if (!navigator.mediaDevices || typeof navigator.mediaDevices.getUserMedia !== "function") {
    setCameraStatus("UNAVAILABLE", "error");
    return;
  }

  if (typeof Hands !== "function" || typeof Camera !== "function") {
    setCameraStatus("SDK LOAD FAILED", "warn");
    return;
  }

  let hands;
  try {
    hands = new Hands({
      locateFile: (file) => `https://cdn.jsdelivr.net/npm/@mediapipe/hands/${file}`
    });
  } catch (error) {
    console.error(error);
    setCameraStatus("SDK INIT FAILED", "warn");
    return;
  }

  hands.setOptions({
    maxNumHands: 1,
    modelComplexity: 1,
    minDetectionConfidence: 0.7,
    minTrackingConfidence: 0.65
  });

  hands.onResults(onHandResults);

  const cameraFeed = new Camera(inputVideo, {
    width: 640,
    height: 480,
    onFrame: async () => {
      await hands.send({ image: inputVideo });
    }
  });

  try {
    await cameraFeed.start();
    setCameraStatus("ACTIVE", "ok");
  } catch (error) {
    console.error(error);
    setCameraStatus("DENIED / BLOCKED", "error");
  }
}

function animate() {
  if (animationStarted) {
    return;
  }

  animationStarted = true;

  const frame = () => {
    requestAnimationFrame(frame);

    modelAnchor.position.x += (targetState.x - modelAnchor.position.x) * 0.18;
    modelAnchor.position.y += (targetState.y - modelAnchor.position.y) * 0.18;
    modelAnchor.rotation.y += (targetState.rotationY - modelAnchor.rotation.y) * 0.16;

    const currentScale = modelAnchor.scale.x;
    const nextScale = currentScale + (targetState.scale - currentScale) * 0.2;
    modelAnchor.scale.setScalar(nextScale);

    if (modelRoot && gestureState.selected) {
      const pulse = (Math.sin(performance.now() * 0.004) + 1) * 0.15 + 0.2;
      modelRoot.traverse((node) => {
        if (node.isMesh && node.material && "emissiveIntensity" in node.material) {
          node.material.emissive = new THREE.Color(0x1f7fb5);
          node.material.emissiveIntensity = pulse;
        }
      });
    }

    renderer.render(scene, camera);
  };

  frame();
}

async function boot() {
  setLoading(true, "Loading 3D CAD model...");

  try {
    await loadModel();
  } catch (error) {
    console.error(error);

    if (!modelRoot) {
      modelRoot = createFallbackModel();
      modelAnchor.add(modelRoot);
    }
  } finally {
    setLoading(false);
    animate();
  }

  await startHandTracking();
}

boot();
