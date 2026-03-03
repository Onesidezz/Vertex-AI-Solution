# AI Search Architecture

```mermaid
flowchart LR
    CM[Content Manager] -->|Records & Documents| API[AI Search API]
    API -->|Process & Search| GCP[Google Vertex AI]
    GCP -->|AI Results| API
    API -->|Search Results| UI[User Interface]
```
