# Architecture

## Recommended shape

```text
.NET MAUI App
  -> Backend/BFF .NET API
    -> AI Orchestrator
      -> OpenAI model
      -> Application tools
        -> User profile service
        -> Reservation service
        -> Recommendation service
        -> Itinerary service
```

## Why this architecture

The mobile app should not own AI logic or secrets. It should only render UI and call your backend.

The backend should:

- authenticate the user
- authorize access to reservations and preferences
- decide which tools/data the model can use
- execute application tools
- run deterministic ranking
- call OpenAI only when necessary
- return structured responses to the app

## Main flow

```text
User asks a question
  -> MAUI sends message to backend
  -> backend identifies user from auth context
  -> backend loads profile and reservations
  -> backend searches candidate recommendations
  -> backend scores candidates deterministically
  -> OpenAI generates explanation and structured response
  -> MAUI renders chat message, cards and quick replies
```

## Important principle

Do not build a generic free-form chatbot first. Build a controlled product feature:

- preferences
- recommendations
- explanations
- plans between reservations
