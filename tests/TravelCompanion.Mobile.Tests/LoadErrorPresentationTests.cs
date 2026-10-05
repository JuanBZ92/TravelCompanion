using System.Net;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Tests
{
    [Collection("Schedule localization")]
    public sealed class LoadErrorPresentationTests : IDisposable
    {
        private readonly string _originalCulture = LocalizationResourceManager.Instance.CurrentCulture.Name;

        [Theory]
        [InlineData("es", false)]
        [InlineData("es", true)]
        [InlineData("en-US", false)]
        [InlineData("en-US", true)]
        public async Task Android_native_transport_errors_use_the_selected_language(string culture, bool useToken)
        {
            LocalizationResourceManager.Instance.SetCulture(culture);
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new NativeSocketException("unexpected end of stream on com.android.okhttp.Address@81541ea7"), useToken);

            Assert.Equal(LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"], viewModel.ErrorMessage);
            Assert.DoesNotContain("okhttp", viewModel.ErrorMessage);
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.IsRefreshing);
        }

        [Theory]
        [InlineData("es", false)]
        [InlineData("es", true)]
        [InlineData("en-US", false)]
        [InlineData("en-US", true)]
        public async Task Android_web_exception_wrapping_native_io_uses_the_connection_message(string culture, bool useToken)
        {
            LocalizationResourceManager.Instance.SetCulture(culture);
            const string message = "unexpected end of stream on com.android.okhttp.Address@6975e0a9";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new WebException(message, new NativeSocketException(message),
                WebExceptionStatus.UnknownError, null), useToken);

            Assert.Equal(LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"], viewModel.ErrorMessage);
            Assert.DoesNotContain("okhttp", viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nested_web_io_wrappers_remain_transport_errors(bool useToken)
        {
            var viewModel = new TestViewModel();
            var native = new WebException("Read failed", new NativeSocketException("EOF"),
                WebExceptionStatus.ReceiveFailure, null);

            await viewModel.FailAsync(new WebException("Read failed", native,
                WebExceptionStatus.UnknownError, null), useToken);

            Assert.Equal(LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"], viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Web_protocol_error_preserves_its_message_even_with_inner_io(bool useToken)
        {
            const string message = "This trip is no longer available.";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new WebException(message, new NativeSocketException("Response detail"),
                WebExceptionStatus.ProtocolError, null), useToken);

            Assert.Equal(message, viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Other_wrappers_preserve_validation_messages_even_with_inner_io(bool useToken)
        {
            const string message = "Refresh this trip before saving.";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new InvalidOperationException(message, new NativeSocketException("Response detail")), useToken);

            Assert.Equal(message, viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Web_exception_without_transport_cause_is_not_reclassified(bool useToken)
        {
            const string message = "The operation is not available.";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new WebException(message, WebExceptionStatus.UnknownError), useToken);

            Assert.Equal(message, viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public async Task Managed_transport_errors_do_not_expose_technical_messages(int kind, bool useToken)
        {
            Exception error = kind switch
            {
                0 => new HttpRequestException("Connection refused"),
                1 => new IOException("Connection reset"),
                _ => new TaskCanceledException("Timeout")
            };
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(error, useToken);

            Assert.Equal(LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"], viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest, false)]
        [InlineData(HttpStatusCode.BadRequest, true)]
        [InlineData(HttpStatusCode.Forbidden, false)]
        [InlineData(HttpStatusCode.Forbidden, true)]
        [InlineData(HttpStatusCode.Conflict, false)]
        [InlineData(HttpStatusCode.Conflict, true)]
        public async Task Http_response_exceptions_keep_the_existing_generic_connection_message(HttpStatusCode status, bool useToken)
        {
            const string message = "Response status code does not indicate success.";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new HttpRequestException(message, new IOException("Response detail"), status), useToken);

            Assert.Equal(LocalizationResourceManager.Instance["AssistantOfflineStatusNoCache"], viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Validation_is_not_classified_by_its_message(bool useToken)
        {
            const string message = "unexpected end of stream is not an allowed title";
            var viewModel = new TestViewModel();

            await viewModel.FailAsync(new InvalidOperationException(message), useToken);

            Assert.Equal(message, viewModel.ErrorMessage);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task User_cancellation_remains_silent(bool useToken)
        {
            var viewModel = new TestViewModel();

            await viewModel.RunAsync(() =>
            {
                viewModel.CancelLoading();
                throw new OperationCanceledException("Cancelled");
            }, useToken);

            Assert.False(viewModel.HasError);
            Assert.False(viewModel.IsBusy);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Successful_retry_clears_the_connection_error(bool useToken)
        {
            var viewModel = new TestViewModel { IsRefreshing = true };
            await viewModel.FailAsync(new NativeSocketException("Connection reset"), useToken);
            Assert.True(viewModel.HasError);

            await viewModel.RunAsync(() => Task.CompletedTask, useToken);

            Assert.False(viewModel.HasError);
            Assert.True(viewModel.HasLoaded);
            Assert.False(viewModel.IsBusy);
            Assert.False(viewModel.IsRefreshing);
        }

        private sealed class TestViewModel : ViewModelBase
        {
            public Task FailAsync(Exception error, bool useToken) => RunAsync(() => throw error, useToken);
            public Task RunAsync(Func<Task> load, bool useToken) => useToken ? LoadAsync(_ => load()) : LoadAsync(load);
        }

        private sealed class NativeSocketException(string message) : Java.IO.IOException(message);

        public void Dispose() => LocalizationResourceManager.Instance.SetCulture(_originalCulture);
    }
}

namespace Java.IO
{
    // Platform boundary: native Android IO has a separate hierarchy from System.IO.
    public class IOException(string message) : Exception(message);
}
