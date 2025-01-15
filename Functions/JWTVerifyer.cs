using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System;
using Microsoft.IdentityModel.Tokens;

namespace AspNetCoreApi.Functions;

public class JWTVerifyer
{
    private readonly string _jwtSecret;

    // Constructor to inject IConfiguration
    public JWTVerifyer(IConfiguration configuration)
    {
        _jwtSecret = configuration["JwtSettings:Secret"];
    }

    public int ExtractUserId(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // Decode and print claims
            List<string> decodedToken = new List<string>();
            Console.WriteLine("Decoded Claims:");
            foreach (var claim in jwt.Claims)
            {
                decodedToken.Add($"{claim.Type}: {claim.Value}");
            }

            // Check expiration
            var expClaim = jwt.Claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim != null)
            {
                // Convert "exp" to DateTime
                var expUnix = long.Parse(expClaim.Value);
                var expDateTime = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;

                if (DateTime.UtcNow > expDateTime)
                {
                    throw new SecurityTokenExpiredException("The token has expired. Please log in again.");
                }
            }
            else
            {
                throw new Exception("The token does not contain an 'exp' claim.");
            }

            // Extract the 'sub' (user ID) claim
            var userIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
            if (userIdClaim == null)
            {
                throw new Exception("The token does not contain a 'sub' claim.");
            }

            int userId = int.Parse(userIdClaim.Value);

            // Return the user ID
            return userId;
        }
        catch (SecurityTokenExpiredException ex)
        {
            Console.WriteLine($"Token expired: {ex.Message}");
            throw new Exception("Token expired: Please log in again.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error decoding token: {ex.Message}");
            throw new Exception($"Error decoding token: {ex.Message}");
        }
    }
}
