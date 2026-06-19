# Coded Values Architecture

## Overview
The system uses a Centralized Source of Truth with Distributed Caching for reference data (Coded Values). 

## Recommended Pattern: Hybrid Architecture
- **Static Values**: Handled via shared type libraries (Enums/Constants).
- **Semi-Stable Values**: Managed via a Coded Values API with local/distributed caching.
- **Flow**: API $\rightarrow$ Distributed Cache (Redis/Memory) $\rightarrow$ Application.

## Implementation Detail
- Store **Codes** (e.g., `GR_10`) in records, not Display Names.
- Use a "Reference Data Pattern" to avoid configuration drift across repositories.
