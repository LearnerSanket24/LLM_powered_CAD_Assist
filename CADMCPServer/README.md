# CADMCPServer - Phase 2 Integration

This backend now includes Sanket LLM orchestration and Shubham Smart CAD analysis under the same endpoint.

## Delivered Phase 2 Scope

- OpenAI and Together AI function-calling integration.
- Ordered MCP planning with assumptions and automatic mass-property queries.
- Self-correction retry loop for recoverable CAD execution errors (max 3 attempts).
- Session memory for multi-turn instructions.
- Smart CAD analysis for `gear`, `shaft`, `bearing`, and generic fallback for unknown components.
- Output structure includes `STATUS -> Metrics -> numbered recommendations -> assumptions_used`.

## API Contract

### POST /assistant/analyze

Request payload (absolute contract field names for geometry and load):

```json
{
  "sessionId": "session-001",
  "userInput": "Create a gear with 24 teeth, module 2.5, face width 22 mm",
  "component_type": "gear",
  "material": "Mild Steel",
  "geometry_input": {
    "module_mm": 2.5,
    "teeth_count": 24,
    "face_width_mm": 22,
    "wall_thickness_mm": 2.5,
    "draft_angle_deg": 1.5,
    "has_undercut": false,
    "thread_pitch_mm": 1.5,
    "projected_area_mm2": 800
  },
  "load_input": {
    "tangential_force_n": 1200,
    "radial_force_n": 400,
    "axial_force_n": 100,
    "torque_nmm": 52000,
    "applied_load_n": 1500
  }
}
```

Response excerpt:

```json
{
  "status": "PASS",
  "message": "STATUS: PASS\nMetrics:\n- bending_stress_mpa: 80.12\n...\nRecommendations:\n1. ...\nassumptions_used:\n- ...",
  "analysis": {
    "component_type": "gear",
    "status": "PASS",
    "metrics": {
      "bending_stress_mpa": 80.12,
      "lewis_form_factor_y": 0.343,
      "safety_factor": 3.12,
      "yield_strength_mpa": 250
    },
    "recommendations": [
      "1. Design is within configured stress and DFM thresholds."
    ],
    "assumptions_used": []
  }
}
```

## Smart CAD Rules

- Gear: Lewis bending stress and safety factor.
- Shaft: torsional stress and equivalent stress based safety factor.
- Bearing: projected contact pressure and safety factor.
- Unknown component: generic projected-load model (accepted by design).
- DFM checks: wall thickness, draft angle, undercut, thread pitch feasibility.

## Configuration

- `Llm.Provider`: `none` | `openai` | `together`
- `Llm.OpenAiApiKey`
- `Llm.TogetherApiKey`
- `Mcp.BaseUrl`
- `Mcp.ToolRoute` (default `/mcp/tool`)
- `Mcp.UseMockResponses`
