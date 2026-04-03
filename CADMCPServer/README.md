# CADMCPServer - Sanket LLM Layer (Phase 2)

This implementation delivers Sanket's scope on top of the Phase 1 backend scaffold.

## Delivered Scope

- OpenAI and Together AI function-calling integration.
- System prompt for parameter extraction and tool planning.
- Ordered MCP tool planner with engineering defaults and explicit assumptions.
- Self-correction loop for recoverable Eyeshot-style errors with max 3 attempts.
- Session context memory for multi-turn updates.
- Assumption reporting in every response.
- Stable API contract for `POST /assistant/analyze`.

## API Contract

### POST /assistant/analyze

Request:

```json
{
  "sessionId": "session-001",
  "userInput": "Create a gear with 20 teeth, module 2, face width 20 mm",
  "modelId": null,
  "overrides": null
}
```

Response (success shape):

```json
{
  "status": "PASS",
  "sessionId": "session-001",
  "attemptsUsed": 1,
  "modelId": "mdl-...",
  "message": "Execution completed with engineering assumptions. Review assumptions list.",
  "assumptions": [
    "Missing face width. Assumed face_width=20 mm."
  ],
  "plannedTools": [
    { "toolName": "create_gear", "arguments": { "teeth": 20, "module": 2, "face_width": 20, "pressure_angle": 20 } },
    { "toolName": "get_volume", "arguments": { "model_id": "$LATEST_MODEL_ID" } },
    { "toolName": "get_mass", "arguments": { "model_id": "$LATEST_MODEL_ID" } },
    { "toolName": "get_surface_area", "arguments": { "model_id": "$LATEST_MODEL_ID" } }
  ],
  "executionTrace": [],
  "lastError": null,
  "context": {
    "lastModelId": "mdl-...",
    "lastComponentType": "gear",
    "lastParameters": {
      "teeth": 20,
      "module": 2,
      "face_width": 20,
      "pressure_angle": 20
    }
  }
}
```

## Configuration

`appsettings.json`

- `Llm.Provider`: `none` | `openai` | `together`
- `Llm.OpenAiApiKey`
- `Llm.TogetherApiKey`
- `Mcp.BaseUrl`
- `Mcp.ToolRoute` (default `/mcp/tool`)
- `Mcp.UseMockResponses` (`true` for early frontend/LLM integration)

## Self-Correction Behavior

- For recoverable CAD errors (for example invalid geometry constraints), planner retries up to 3 attempts.
- On retry, the plan is regenerated via LLM if available, otherwise deterministic repair rules adjust risky parameters.
- Failure after 3 attempts returns `status=FAIL` with structured `lastError` and full `executionTrace`.

## Multi-turn Context Example

1) User: `Create gear with 24 teeth module 2`
2) User: `Now increase module to 2.5`

The second request reuses `sessionId` and applies `modify_dim` against the last model in session state.

## Notes

- .NET SDK was not available in this machine during implementation, so build/run could not be executed here.
- Static diagnostics in editor report no code errors.
