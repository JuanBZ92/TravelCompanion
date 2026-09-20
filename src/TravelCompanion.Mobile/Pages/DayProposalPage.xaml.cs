using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Shared.Dtos;
namespace TravelCompanion.Mobile.Pages;
[QueryProperty(nameof(Proposal), "Proposal")]
public partial class DayProposalPage : ContentPage
{
    private readonly DayProposalViewModel _viewModel;
    public DayProposalDto? Proposal { set { if (value is not null) _viewModel.SetProposal(value); } }
    public DayProposalPage(DayProposalViewModel viewModel) { InitializeComponent(); BindingContext = _viewModel = viewModel; }
}
