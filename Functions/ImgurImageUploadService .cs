using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public class ImgurImageUploadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _imgurClientId;

    public ImgurImageUploadService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _imgurClientId = configuration["ImgurClientId"];
    }

    public async Task<string> UploadImageAsync(Stream imageStream, string contentType)
    {
        // Convert the uploaded image into a base64 string
        var imageBytes = new byte[imageStream.Length];
        await imageStream.ReadAsync(imageBytes, 0, (int)imageStream.Length);
        var base64Image = Convert.ToBase64String(imageBytes);

        // Prepare the HttpClient
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Client-ID", _imgurClientId);

        // Prepare the content
        var content = new MultipartFormDataContent();
        content.Add(new StringContent(base64Image), "image");

        // Send the image to Imgur
        var response = await client.PostAsync("https://api.imgur.com/3/upload", content);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception("Error uploading image to Imgur: " + response.ReasonPhrase);
        }

        // Parse the response content
        var responseContent = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseContent);
        var imageUrl = json.RootElement.GetProperty("data").GetProperty("link").GetString();

        return imageUrl;
    }
}
