# SRS Coverage Report (2026-04-04)

This report maps the March 2026 SRS to the current CADMCPServer implementation.

## Scope Summary

- LLM parsing/planning layer: implemented
- MCP tool dispatch layer: implemented
- CAD engine prototype layer: implemented (in-memory approximation)
- Smart CAD assistant analysis layer: implemented
- Web UI + API hosting: implemented

## Requirement Group Status

| Group | Status |
|---|---|
| FR-LLM-01..05 | Implemented |
| FR-MCP-01..04 | Implemented |
| FR-CAD-01..05 | Implemented (prototype for geometry/export fidelity) |
| FR-SCA-01..05 | Implemented |
| NFR (performance/reliability/usability/maintainability/security) | Partial |
| Acceptance Criteria AC-01..AC-05 | Mostly implemented; AC-03 and AC-05 are prototype-grade |

## Evidence In Code

- Assistant API: Controllers/AssistantController.cs
- MCP APIs: Controllers/McpController.cs
- Model APIs: Controllers/ModelController.cs
- LLM client: Services/Llm/FunctionCallingLlmClient.cs
- Tool planner + retry planning: Services/Planning/ToolPlanner.cs
- Orchestration + self-correction loop: Services/Assistant/AssistantOrchestrator.cs
- Smart CAD analysis + DFM checks: Services/Assistant/SmartCadAnalyzer.cs
- CAD tool execution: Services/Cad/InMemoryCadEngine.cs
- Tool schemas: McpToolSchemas.json

## Gaps Against SRS v1.0 (High Priority)

1. Real CAD kernel integration
- Current: in-memory geometric approximations
- SRS asks for Eyeshot-based solid modeling and accurate geometric operations

2. Export fidelity
- Current: export tools are placeholders/prototype responses
- SRS asks for valid STEP and STL output files

3. Stronger assembly math
- Current: clearance/interference are simplified
- SRS asks for robust assembly validation behavior

4. Config externalization
- Current: core material and rule constants live in code
- SRS asks editable configuration-driven material/rule database

5. Hardening NFRs
- Missing: rate limiting, structured auth boundaries, explicit session file isolation policy for exports

## Immediate Next Implementation Slice

1. Replace prototype export stubs with real file writers and output directory policy.
2. Move materials + DFM thresholds into config and wire through options.
3. Add request throttling middleware and explicit per-session output folders.
4. Add automated acceptance tests for AC-01..AC-05.

## Current Run State

Verified as healthy during this audit:
- GET /health -> 200
- GET /mcp/schemas -> 200
