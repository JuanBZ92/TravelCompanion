using TravelCompanion.Api.Services;

namespace TravelCompanion.Api.Tests;

public sealed class TravelChatIntentClassifierTests
{
    private readonly TravelChatIntentClassifier _classifier = new();

    [Theory]
    [InlineData("proponme un plan")]
    [InlineData("dime un plan")]
    [InlineData("quiero un plan")]
    [InlineData("propron un plan")]
    [InlineData("proprn un plan")]
    [InlineData("recomiendame algo")]
    [InlineData("sugiereme algo para hoy")]
    [InlineData("fabrica un plan")]
    [InlineData("fabricame algo para manana")]
    [InlineData("armame algo para el martes")]
    [InlineData("recomendar plan para caminar")]
    [InlineData("recomdar plan para pareja")]
    [InlineData("recomendar plan nocturno")]
    [InlineData("recomendar plan para bailar")]
    public void Classify_detects_plan_intent_variants(string message)
    {
        var result = _classifier.Classify(message);

        Assert.Equal(TravelChatIntents.Plan, result.Intent);
        Assert.True(result.IsPlanning);
        Assert.True(result.Confidence > 0);
    }

    [Theory]
    [InlineData("ver mi agenda", TravelChatIntents.ViewSchedule)]
    [InlineData("que tengo manana", TravelChatIntents.ViewSchedule)]
    [InlineData("ver mi agnda", TravelChatIntents.ViewSchedule)]
    [InlineData("ver mis preferencias", TravelChatIntents.ViewPreferences)]
    [InlineData("ver mis preferencas", TravelChatIntents.ViewPreferences)]
    [InlineData("editar preferencia evitar culture", TravelChatIntents.ViewPreferences)]
    [InlineData("guardar este plan", TravelChatIntents.SaveItinerary)]
    [InlineData("guadar este plan", TravelChatIntents.SaveItinerary)]
    [InlineData("que puedo pedirte", TravelChatIntents.Help)]
    [InlineData("ayudaa", TravelChatIntents.Help)]
    public void Classify_detects_non_plan_intents(string message, string expectedIntent)
    {
        var result = _classifier.Classify(message);

        Assert.Equal(expectedIntent, result.Intent);
    }

    [Fact]
    public void Classify_preserves_planning_signal_when_preference_change_is_embedded_in_plan_request()
    {
        var result = _classifier.Classify("proponeme un plan para 2026-10-06 evitando culture");

        Assert.Equal(TravelChatIntents.ViewPreferences, result.Intent);
        Assert.True(result.IsPlanning);
    }

    [Fact]
    public void Classify_does_not_treat_plain_preference_update_as_plan()
    {
        var result = _classifier.Classify("editar preferencia evitar culture");

        Assert.Equal(TravelChatIntents.ViewPreferences, result.Intent);
        Assert.False(result.IsPlanning);
    }

    [Fact]
    public void Classify_returns_observable_signals_and_confidence()
    {
        var result = _classifier.Classify("recomiendame algo con menos caminata");

        Assert.Equal(TravelChatIntents.Plan, result.Intent);
        Assert.True(result.Confidence > 0);
        Assert.Equal(TravelChatResponseModes.LessWalking, result.ResponseMode);
        Assert.NotEmpty(result.MatchedSignals);
    }

    [Theory]
    [InlineData("proponme un plan de coste bajo", TravelChatResponseModes.Cheaper)]
    [InlineData("dame algo de costo bajo", TravelChatResponseModes.Cheaper)]
    [InlineData("quiero un plan de coste medio", TravelChatResponseModes.MediumCost)]
    [InlineData("dime un plan de costo alto", TravelChatResponseModes.HighCost)]
    [InlineData("fabrica un plan premium", TravelChatResponseModes.HighCost)]
    public void Classify_detects_cost_response_modes(string message, string expectedResponseMode)
    {
        var result = _classifier.Classify(message);

        Assert.Equal(TravelChatIntents.Plan, result.Intent);
        Assert.Equal(expectedResponseMode, result.ResponseMode);
    }

    [Theory]
    [InlineData("plan para comer", TravelChatResponseModes.Food)]
    [InlineData("proponeme un plan para relajar", TravelChatResponseModes.LessWalking)]
    [InlineData("recomendar por cercania teniendo en cuenta el pedido inicial", TravelChatResponseModes.LessWalking)]
    [InlineData("recomendar por duracion teniendo en cuenta el pedido inicial", TravelChatResponseModes.Shorter)]
    public void Classify_detects_guided_chip_response_modes(string message, string expectedResponseMode)
    {
        var result = _classifier.Classify(message);

        Assert.Equal(TravelChatIntents.Plan, result.Intent);
        Assert.Equal(expectedResponseMode, result.ResponseMode);
    }
}
