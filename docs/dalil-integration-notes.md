# Dalil integration notes

Dalil is an isolated, server-side AI assistant module. This branch deliberately does not register it in `Program.cs`, render it from the shared layout, or add a header control.

## A. Shared UI integration

The prepared partial is:

`Views/Shared/Dalil/_DalilAssistantWidget.cshtml`

After owner approval, render it once near the end of `Views/Shared/_Layout.cshtml`, before the closing `body` tag:

```cshtml
<partial name="Dalil/_DalilAssistantWidget" />
```

Add one icon-only button to the existing header. Its click handler should call:

```javascript
window.DalilAssistant.open();
```

Use an accessible Arabic label such as `aria-label="فتح مساعد دليل"`. Do not create another global chat or duplicate the partial on individual pages.

## B. DI registration

Add this namespace and one registration call in `Program.cs`:

```csharp
using FixPal.Features.Dalil;

builder.Services.AddDalilAssistant(builder.Configuration);
```

The extension registers `IDalilAssistantService` through a typed `HttpClient` backed by `GeminiDalilAssistantService`, plus the narrow read-only context and recommendation services. It reuses the application's existing `ProviderMatchingService` registration.

## C. Configuration

Supported configuration keys:

| Key | Purpose | Default |
| --- | --- | --- |
| `Dalil:GeminiApiKey` | Server-side Gemini credential | none |
| `Dalil:Model` | Gemini model name | `gemini-3.5-flash-lite` |
| `Dalil:Endpoint` | Gemini REST base URL | `https://generativelanguage.googleapis.com/v1beta/` |
| `Dalil:TimeoutSeconds` | Per-request timeout | `20` |
| `Dalil:MaxOutputTokens` | Output token ceiling | `1024` |
| `Dalil:Temperature` | Generation temperature | `0.2` |

Store a development credential with user secrets:

```powershell
dotnet user-secrets set "Dalil:GeminiApiKey" "<your-key>"
```

The environment-variable equivalent is:

```text
Dalil__GeminiApiKey=<your-key>
```

Never add a real credential to `appsettings.json`, Razor, JavaScript, logs, source control, or a URL. The provider sends it from the server in the `x-goog-api-key` request header, following the [official Gemini authentication guidance](https://ai.google.dev/gemini-api/docs/api-key).

## D. Gemini-enabled test

After the owner approves the shared-file changes:

1. Set `Dalil:GeminiApiKey` through user secrets or the environment.
2. Optionally set `Dalil:Model` to an enabled Gemini model.
3. Add the DI call and render the partial as described above.
4. Start the application and sign in.
5. Open Dalil from the single header icon and send a short maintenance question.
6. Confirm the browser calls only `/DalilAssistant/Send`; the Gemini request must originate from the server.

The provider uses Gemini `models.generateContent`, `systemInstruction`, and JSON structured output as described in the [official generateContent reference](https://ai.google.dev/api/generate-content).

## E. Unavailable behavior

When the key is absent, configuration is invalid, Gemini times out, Gemini returns a non-success status, or its response is malformed, the module returns a generic Arabic unavailable response. Provider bodies, stack traces, configuration details, and credentials are not returned to the browser. Caller-initiated cancellation is preserved.

## F. Future safe context

`DalilSafeContextService` uses explicit no-tracking projections to load only service-category names/descriptions and active city/area names. Gemini receives only those catalog values and never receives provider records. AI category and location suggestions are matched back to that catalog before the existing `ProviderMatchingService` can run. Provider recommendations are returned directly by the backend through a dedicated public DTO.

Gemini never receives `ApplicationDbContext`, unrestricted queries, EF entities, provider records, private request data, customer contact data, message history, or credentials. The context service has narrow read-only database access and cannot perform arbitrary AI-directed queries.

## G. Future Create Request replacement

After the global assistant is approved and integrated, the old request-creation AI/chat experience may open this same Dalil panel with approved safe context. It should not remain as a second competing AI chat. This replacement is intentionally outside the isolated branch.
