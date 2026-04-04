const promptInput = document.getElementById("promptInput");
const runButton = document.getElementById("runButton");
const sampleButton = document.getElementById("sampleButton");
const statusLabel = document.getElementById("status");
const analysisStatus = document.getElementById("analysisStatus");
const metricsNode = document.getElementById("metrics");
const recommendationsNode = document.getElementById("recommendations");
const assumptionsNode = document.getElementById("assumptions");
const traceOutput = document.getElementById("traceOutput");
const canvas = document.getElementById("viewerCanvas");

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(50, 1, 0.1, 100);
camera.position.set(0, 1.4, 4.2);

const ambient = new THREE.AmbientLight(0x99cfff, 0.6);
scene.add(ambient);

const dir = new THREE.DirectionalLight(0xffffff, 1.1);
dir.position.set(2, 4, 3);
scene.add(dir);

const ground = new THREE.Mesh(
  new THREE.CircleGeometry(6, 72),
  new THREE.MeshStandardMaterial({ color: 0x0c1c30, roughness: 0.85, metalness: 0.15 })
);
ground.rotation.x = -Math.PI / 2;
ground.position.y = -1.2;
scene.add(ground);

const modelGroup = new THREE.Group();
scene.add(modelGroup);

function resize() {
  const w = canvas.clientWidth;
  const h = canvas.clientHeight;
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

window.addEventListener("resize", resize);
resize();

function clearModel() {
  while (modelGroup.children.length) {
    const child = modelGroup.children.pop();
    child.geometry?.dispose?.();
    child.material?.dispose?.();
  }
}

function buildGear(params) {
  const group = new THREE.Group();
  const teeth = Math.max(8, Number(params.teeth || 20));
  const moduleVal = Number(params.module || 2);
  const faceWidth = Number(params.face_width || 20) * 0.04;
  const outerR = ((moduleVal * (teeth + 2)) * 0.5) * 0.04;
  const boreR = Math.max(0.03, Number(params.bore_dia || 8) * 0.5 * 0.04);

  const body = new THREE.Mesh(
    new THREE.CylinderGeometry(outerR, outerR, faceWidth, 96),
    new THREE.MeshStandardMaterial({ color: 0x5ccaff, metalness: 0.55, roughness: 0.35 })
  );
  body.rotation.x = Math.PI / 2;
  group.add(body);

  const bore = new THREE.Mesh(
    new THREE.CylinderGeometry(boreR, boreR, faceWidth * 1.2, 48),
    new THREE.MeshStandardMaterial({ color: 0x0e243e, metalness: 0.2, roughness: 0.7 })
  );
  bore.rotation.x = Math.PI / 2;
  group.add(bore);

  const toothW = Math.max(0.02, outerR * 0.18);
  const toothH = Math.max(0.02, outerR * 0.16);
  const toothD = Math.max(0.03, faceWidth * 0.95);

  for (let i = 0; i < teeth; i += 1) {
    const angle = (i / teeth) * Math.PI * 2;
    const tooth = new THREE.Mesh(
      new THREE.BoxGeometry(toothW, toothH, toothD),
      new THREE.MeshStandardMaterial({ color: 0x8ee4ff, metalness: 0.55, roughness: 0.3 })
    );

    const radius = outerR + toothH * 0.45;
    tooth.position.set(Math.cos(angle) * radius, Math.sin(angle) * radius, 0);
    tooth.lookAt(0, 0, 0);
    tooth.rotateX(Math.PI / 2);
    group.add(tooth);
  }

  return group;
}

function buildShaft(params) {
  const diameter = Number(params.diameter || 20) * 0.04;
  const length = Number(params.length || 100) * 0.03;

  const shaft = new THREE.Mesh(
    new THREE.CylinderGeometry(diameter * 0.5, diameter * 0.5, length, 72),
    new THREE.MeshStandardMaterial({ color: 0xa8d7ff, metalness: 0.45, roughness: 0.35 })
  );
  shaft.rotation.x = Math.PI / 2;
  return shaft;
}

function buildBearing(params) {
  const outer = Number(params.outer_diameter || 47) * 0.02;
  const inner = Number(params.inner_diameter || 20) * 0.02;
  const width = Number(params.width || 14) * 0.03;

  const ring = new THREE.Mesh(
    new THREE.CylinderGeometry(outer, outer, width, 72),
    new THREE.MeshStandardMaterial({ color: 0x95cfff, metalness: 0.5, roughness: 0.35 })
  );
  ring.rotation.x = Math.PI / 2;

  const voidPart = new THREE.Mesh(
    new THREE.CylinderGeometry(inner, inner, width * 1.2, 48),
    new THREE.MeshStandardMaterial({ color: 0x10253c, metalness: 0.2, roughness: 0.8 })
  );
  voidPart.rotation.x = Math.PI / 2;

  const group = new THREE.Group();
  group.add(ring);
  group.add(voidPart);
  return group;
}

function buildModel(componentType, params) {
  switch ((componentType || "").toLowerCase()) {
    case "gear":
      return buildGear(params);
    case "shaft":
      return buildShaft(params);
    case "bearing":
      return buildBearing(params);
    default:
      return new THREE.Mesh(
        new THREE.TorusKnotGeometry(0.75, 0.22, 220, 30),
        new THREE.MeshStandardMaterial({ color: 0x6edbff, metalness: 0.55, roughness: 0.35 })
      );
  }
}

function renderMetrics(metrics) {
  metricsNode.innerHTML = "";
  const entries = Object.entries(metrics || {});
  if (entries.length === 0) {
    metricsNode.textContent = "No metrics available.";
    return;
  }

  entries.forEach(([key, value]) => {
    const row = document.createElement("div");
    row.className = "kv";
    row.innerHTML = `<span>${key}</span><strong>${Number(value).toFixed(4)}</strong>`;
    metricsNode.appendChild(row);
  });
}

function renderList(node, values) {
  node.innerHTML = "";
  const list = values && values.length ? values : ["No entries."];
  list.forEach((value) => {
    const li = document.createElement("li");
    li.textContent = value;
    node.appendChild(li);
  });
}

function setStatus(text, cls = "") {
  statusLabel.textContent = text;
  statusLabel.className = "status";
  if (cls) {
    statusLabel.classList.add(cls);
  }
}

function updateChip(status) {
  analysisStatus.textContent = `STATUS: ${status || "N/A"}`;
  analysisStatus.className = "chip";

  if (status === "PASS") {
    analysisStatus.classList.add("ok");
  } else if (status === "WARNING") {
    analysisStatus.classList.add("warn");
  } else if (status === "FAIL") {
    analysisStatus.classList.add("error");
  }
}

async function runPrompt() {
  const userInput = promptInput.value.trim();
  if (!userInput) {
    setStatus("Please enter a prompt.", "error");
    return;
  }

  runButton.disabled = true;
  setStatus("Running LLM + MCP pipeline...", "warn");

  try {
    const response = await fetch("/assistant/analyze", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        sessionId: "demo-session",
        userInput,
        component_type: undefined,
        material: "Mild Steel"
      })
    });

    const payload = await response.json();

    if (!response.ok) {
      throw new Error(payload?.message || "Assistant failed.");
    }

    const componentType = payload?.context?.lastComponentType || payload?.analysis?.component_type || "generic";
    const params = payload?.context?.lastParameters || {};

    clearModel();
    const model = buildModel(componentType, params);
    modelGroup.add(model);

    renderMetrics(payload?.analysis?.metrics || {});
    renderList(recommendationsNode, payload?.analysis?.recommendations || []);
    renderList(assumptionsNode, payload?.analysis?.assumptions_used || payload?.assumptions || []);

    traceOutput.textContent = JSON.stringify(payload.executionTrace || [], null, 2);
    updateChip(payload.status || payload?.analysis?.status);
    setStatus(`Model ${payload.modelId || "(no id)"} generated successfully.`, "ok");
  } catch (error) {
    console.error(error);
    setStatus(error.message || "Pipeline execution failed.", "error");
  } finally {
    runButton.disabled = false;
  }
}

sampleButton.addEventListener("click", () => {
  promptInput.value = "Create a gear with 20 teeth, module 2, face width 20mm, bore dia 8mm";
});

runButton.addEventListener("click", runPrompt);

function animate() {
  requestAnimationFrame(animate);
  modelGroup.rotation.y += 0.003;
  renderer.render(scene, camera);
}

sampleButton.click();
animate();
