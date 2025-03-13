namespace RussiaBasketBot;

public class Utils
{
    public static async Task<string?> GetRedirectLocationAsync(string url)
    {
        using var handler = new HttpClientHandler();

        handler.AllowAutoRedirect = false;

        using var client = new HttpClient(handler);
        
        // Send a GET request
        var response = await client.GetAsync(url);

        // Check if the response has a redirect status code (3xx)
        if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
        {
            // Extract the Location header
            return response.Headers.Location != null ? response.Headers.Location.ToString() : null;
        }

        return null;
    }
}