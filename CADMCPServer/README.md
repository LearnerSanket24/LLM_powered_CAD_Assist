# LLM-Powered CAD MCP Prototype

Complete prototype with 5-layer architecture:

1. Frontend Web UI (Three.js) in `wwwroot`
2. LLM layer (intent parsing + tool planning)
3. MCP server (tool dispatcher) at `/mcp/tool`
4. CAD engine layer (in-memory Eyeshot-compatible prototype)
5. Smart CAD assistant (stress + safety + DFM)

## Architecture Flow

`User Input -> /assistant/analyze -> Planner -> MCP Tool Calls -> CAD Engine -> Smart Analysis -> UI`

## Implemented Endpoints

1. `GET /health`
2. `GET /mcp/schemas`
3. `POST /mcp/tool`
4. `POST /assistant/analyze`
5. `GET /model/{id}`
6. `GET /model/{id}/render`

## MCP Tools

Creation:

1. `create_gear(teeth, module, face_width, pressure_angle=20, bore_dia, material)`
2. `create_shaft(length, diameter, material)`
3. `create_bearing(inner_diameter|inner_dia, outer_diameter|outer_dia, width, material)`

Modification:

1. `modify_dim(model_id, param, value)`
2. `add_fillet(model_id, edge_id, radius)`
3. `add_chamfer(model_id, edge_id, distance)`

Query:

1. `get_volume(model_id)`
2. `get_mass(model_id, material)`
3. `get_surface_area(model_id)`

Assembly:

1. `measure_clearance(model_id_a, model_id_b)`
2. `check_interference(model_id_a, model_id_b)`

Export:

1. `export_step(model_id, file_path)`
2. `export_stl(model_id, file_path)`
3. `render_viewport(model_id, angle)`

Tool schema JSON file:

`McpToolSchemas.json`

## Smart CAD Analysis

1. Lewis bending stress: `sigma = Ft / (b * m * Y)`
2. Safety factor and recommendations
3. Material DB:
   1. Mild Steel: 210 MPa
  2. Medium Carbon Steel: 420 MPa
   3. Alloy Steel: 850 MPa
  4. Aluminium 6061-T6: 276 MPa
  5. Nylon PA66: 85 MPa
4. DFM checks:
   1. Wall thickness
   2. Undercuts
   3. Draft angles
   4. Thread pitch

## Frontend

Static UI is hosted from:

1. `wwwroot/index.html`
2. `wwwroot/style.css`
3. `wwwroot/app.js`

Features:

1. Natural language input box
2. Three.js 3D model rendering
3. Live response panel with status, metrics, recommendations
4. Execution trace panel

## Run Locally

Prerequisites:

1. .NET 8 SDK+

Commands:

1. `cd CADMCPServer`
2. `dotnet restore`
3. `dotnet run`

Open:

1. `http://localhost:5050` when using `--urls "http://localhost:5050"`
2. Otherwise use the URL printed by `dotnet run`

## LLM Setup

Configure in `appsettings.json` or environment variables:

1. `Llm.Provider` = `openai` or `together`
2. `Llm.OpenAiApiKey`
3. `Llm.TogetherApiKey`
4. `Analysis.*` for materials, Lewis factors, and DFM thresholds
5. `Output.*` for session-isolated export directory policy
6. `Throttle.*` for API request throttling

## Example API Calls

### 1) Execute MCP Tool

`POST /mcp/tool`

```json
{
  "tool_name": "create_gear",
  "arguments": {
    "teeth": 20,
    "module": 2,
    "face_width": 20,
    "pressure_angle": 20,
    "bore_dia": 8,
    "material": "Mild Steel"
  }
}
```

### 2) Assistant End-to-End

`POST /assistant/analyze`

```json
{
  "sessionId": "demo-1",
  "userInput": "Create a gear with 20 teeth, module 2, face width 20mm, bore 8mm",
  "material": "Mild Steel",
  "geometry_input": {
    "module_mm": 2,
    "teeth_count": 20,
    "face_width_mm": 20,
    "wall_thickness_mm": 2.5,
    "draft_angle_deg": 1.2,
    "has_undercut": false,
    "thread_pitch_mm": 1.5,
    "projected_area_mm2": 700
  },
  "load_input": {
    "tangential_force_n": 900,
    "radial_force_n": 250,
    "axial_force_n": 100,
    "torque_nmm": 42000,
    "applied_load_n": 1200
  }
}
```

### Sample Response (trimmed)

```json
{
  "status": "PASS",
  "modelId": "gear-...",
  "analysis": {
    "status": "PASS",
    "metrics": {
      "bending_stress_mpa": 80.1,
      "safety_factor": 2.62
    },
    "recommendations": [
      "1. Design is within configured stress and DFM thresholds."
    ]
  },
  "executionTrace": [
    { "toolName": "create_gear", "success": true },
    { "toolName": "get_volume", "success": true },
    { "toolName": "get_mass", "success": true },
    { "toolName": "get_surface_area", "success": true }
  ]
}
```

## Notes

1. CAD engine is an Eyeshot-compatible prototype implementation for hackathon/local use.
2. Swap `InMemoryCadEngine` with real Eyeshot SDK service for production geometry kernels.
3. Export and viewport artifacts are written under `bin/Debug/net8.0/outputs/{session_id}/` by default.
4. Keep API keys in environment variables; do not commit secrets into appsettings files.
