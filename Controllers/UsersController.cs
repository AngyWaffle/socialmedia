using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Linq;
using System;
using BCrypt.Net;  // bcrypt for password hashing
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using AspNetCoreApi.Functions;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly ImgurImageUploadService _imgurImageUploadService;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _context;
    private readonly JWTVerifyer _jwtVerifyer;

    public UserController(ImgurImageUploadService imgurImageUploadService, ApplicationDbContext context, IConfiguration configuration, JWTVerifyer jwtVerifyer)
    {
        _imgurImageUploadService = imgurImageUploadService;
        _context = context;
        _configuration = configuration;
        _jwtVerifyer = jwtVerifyer;

    }

    [HttpGet("getUser")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users.ToListAsync();
        return Ok(users);
    }

    [HttpPost("jwtTest")]
    public IActionResult JWTTest()
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);

        return Ok(userId);
    }

    [HttpPost("createUser")]
    public async Task<IActionResult> AddUser(UserCreate createuser)
    {
        // Check if Username or Email already exists before attempting to add
        if (_context.Users.Any(u => u.Username == createuser.Username))
        {
            return BadRequest(new { message = "Username already exists" });
        }

        if (_context.Users.Any(u => u.Mail == createuser.Mail))
        {
            return BadRequest(new { message = "Email already exists" });
        }

        // Create a new User object and map properties from CreateUser
        var user = new User
        {
            Username = createuser.Username,
            Password = createuser.Password,
            Mail = createuser.Mail,
            Bio = null, // Initialize to default values
            ProfileImageUrl = null,
            PreferredTags = null,
            FollowingUsers = null,
            LikedPosts = null
        };


        // Ensure the user's password is hashed before storing it
        user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);

        _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync();
            return Ok(true);
        }
        catch (Exception ex)
        {
            // Log the actual exception for debugging purposes (optional)
            Console.WriteLine(ex.Message);

            // Return a general error message to the client
            return StatusCode(500, new { message = "An error occurred while creating the user." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Retrieve the user by username
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.Password))
        {
            return Unauthorized(new { message = "Invalid username or password" });
        }

        // Generate a JWT token
        var jwtSecret = _configuration["JwtSettings:Secret"];
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.Now.AddHours(6),
            signingCredentials: creds);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        Response.Cookies.Append("auth_token", tokenString, new CookieOptions
        {
            HttpOnly = true,        // Prevent JavaScript access
            Secure = false,          // Allow only over HTTPS
            SameSite = SameSiteMode.Lax, // Prevent CSRF attacks
            Expires = DateTime.UtcNow.AddHours(6) // Set expiry
        });

        Console.WriteLine(tokenString);

        // Return the JWT token to the client
        return Ok(true);
    }

    [HttpPost("editBio")]
    public async Task<IActionResult> EditBio([FromBody] EditBioRequest request)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "User not found" });
        }

        user.Bio = request.Bio;

        try
        {
            await _context.SaveChangesAsync();
            return Ok(new { message = "Bio updated successfully", user.Bio });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return StatusCode(500, new { message = "An error occurred while updating the bio." });
        }
    }

    [HttpPost("uploadProfileImage")]
    public async Task<IActionResult> UploadProfileImage(IFormFile image)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);
        
        if (image == null || image.Length == 0)
        {
            return BadRequest("No image uploaded.");
        }

        // Upload image to Imgur
        using var stream = image.OpenReadStream();
        string imageUrl;
        try
        {
            imageUrl = await _imgurImageUploadService.UploadImageAsync(stream, image.ContentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error uploading image: " + ex.Message);
        }

        // Find the user by ID and update their ProfileImageUrl
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        user.ProfileImageUrl = imageUrl;

        // Save changes to the database
        try
        {
            await _context.SaveChangesAsync();
            return Ok(new { message = "Profile image updated successfully", imageUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error updating profile image URL in the database: " + ex.Message);
        }
    }
    [HttpPost("createPost")]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest request)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);
        string? imageUrl = null;

        // Check if the request has ImageBase64
        if (!string.IsNullOrEmpty(request.ImageBase64))
        {
            try
            {
                // Convert Base64 string to byte array
                byte[] imageBytes = Convert.FromBase64String(request.ImageBase64);

                // Convert byte array to a stream
                using var stream = new MemoryStream(imageBytes);

                // Upload image to Imgur
                imageUrl = await _imgurImageUploadService.UploadImageAsync(stream, "image/jpeg"); // or the appropriate MIME type
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error uploading image: " + ex.Message);
            }
        }

        // Check if the user exists
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Create the new post
        var newPost = new Posts
        {
            UserId = userId,
            Content = request.Content,
            ImageUrl = imageUrl,
            Username = user.Username,
            CreatedAt = DateTime.UtcNow,
            Tags = request.Tags,
        };

        _context.Posts.Add(newPost);

        try
        {
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(CreatePost), new { id = newPost.Id }, newPost);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return StatusCode(500, new { message = "An error occurred while creating the post." });
        }
    }
    [HttpGet("getPosts")]
    public async Task<IActionResult> GetPosts()
    {
        var postDetailsList = await _context.Posts
            .Select(posts => new PostDetails
            {
                Posts = posts, // Assign each Posts entity to the Posts property in PostDetails
                Comments = _context.Comments
                    .Where(comment => comment.PostId == posts.Id)
                    .ToList(),
                Likes = _context.Likes
                    .Count(like => like.PostId == posts.Id)
            })
            .ToListAsync();

        return Ok(postDetailsList);
    }
    [HttpPost("toggleLike")]
    public async Task<IActionResult> ToggleLike(int postId)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);
        // Check if the post exists
        var post = await _context.Posts.FindAsync(postId);
        if (post == null)
        {
            return NotFound(new { message = "Post not found." });
        }

        // Check if the like already exists
        var existingLike = await _context.Likes
            .FirstOrDefaultAsync(l => l.UserId == userId && l.PostId == postId);

        if (existingLike != null)
        {
            // Like exists, so remove it (unlike)
            _context.Likes.Remove(existingLike);

            // Remove the corresponding interaction
            var existingInteraction = await _context.UserInteraction
                .FirstOrDefaultAsync(ui => ui.UserId == userId && ui.PostId == postId && ui.InteractionType == "like");
            if (existingInteraction != null)
            {
                _context.UserInteraction.Remove(existingInteraction);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Like removed." });
        }
        else
        {
            // Like doesn't exist, so add it
            var like = new Likes
            {
                UserId = userId,
                PostId = postId
            };
            _context.Likes.Add(like);

            // Add a new interaction
            var interaction = new UserInteraction
            {
                UserId = userId,
                PostId = postId,
                InteractionTime = DateTime.Now,
                InteractionType = "like"
            };
            _context.UserInteraction.Add(interaction);

            // Fetch tags for the post
            var postEntity = await _context.Posts
                .Where(p => p.Id == postId)
                .FirstOrDefaultAsync();

            if(postEntity == null)
            {
                throw new Exception("Post not found");
            }

            if (postEntity?.Tags == null)
            {
                return Ok(new { message = "Like added." });
            }

            var prefTags = postEntity.Tags;

            // Fetch the user to update preferred tags
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user.PreferredTags == null)
            {
                user.PreferredTags = new List<string>();
            }
            var uniqueTags = prefTags.Except(user.PreferredTags).ToList();
            user.PreferredTags.AddRange(uniqueTags);

            await _context.SaveChangesAsync();
            return Ok(new { message = "Like added." });
        }
    }
    [HttpPost("addViews")]
    public async Task<IActionResult> AddViews([FromBody] List<int> postIds)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);

        // Check if the list of postIds is empty or null
        if (postIds == null || postIds.Count == 0)
        {
            return BadRequest(new { message = "No post IDs provided." });
        }

        var interactions = new List<UserInteraction>();

        foreach (var postId in postIds)
        {
            // Check if the post exists
            var post = await _context.Posts.FindAsync(postId);
            if (post == null)
            {
                // Skip this post ID if it doesn't exist
                continue;
            }

            // Create a new interaction for the post
            interactions.Add(new UserInteraction
            {
                UserId = userId,
                PostId = postId,
                InteractionTime = DateTime.Now,
                InteractionType = "view"
            });
        }

        // Add all interactions to the database in one operation
        if (interactions.Count > 0)
        {
            _context.UserInteraction.AddRange(interactions);
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = $"{interactions.Count} views added." });
    }

    [HttpPost("toggleFollow")]
    public async Task<IActionResult> ToggleFollow(int followedId)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var followerId = _jwtVerifyer.ExtractUserId(token);
        // Check if the post exists
        var user = await _context.Users.FindAsync(followedId);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Check if the like already exists
        var existingFollow = await _context.Followers
            .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowedId == followedId);

        if (existingFollow != null)
        {
            // Like exists, so remove it (unlike)
            _context.Followers.Remove(existingFollow);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Unfollowed" });
        }
        else
        {
            // Like doesn't exist, so add it
            var follow = new Followers
            {
                FollowerId = followerId,
                FollowedId = followedId
            };

            _context.Followers.Add(follow);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Followed" });
        }
    }
    [HttpGet("getLikes")]
    public async Task<IActionResult> GetLikes(int postId)
    {
        // Check if the post exists
        var post = await _context.Posts.FindAsync(postId);
        if (post == null)
        {
            return NotFound(new { message = "Post not found." });
        }

        // Retrieve likes for the specified postId
        var likes = await _context.Likes
            .Where(like => like.PostId == postId)
            .ToListAsync();
        return Ok(likes.Count);
    }
    [HttpGet("getFollows")]
    public async Task<IActionResult> GetFollows(int userId)
    {
        // Check if the post exists
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        // Retrieve likes for the specified postId
        var follow = await _context.Followers
            .Where(follow => follow.FollowedId == userId)
            .ToListAsync();
        return Ok(follow.Count);
    }
    [HttpGet("followList")]
    public async Task<IActionResult> FollowList()
    {
        var follows = await _context.Followers.ToListAsync();
        return Ok(follows);
    }
    [HttpPost("newComment")]
    public async Task<IActionResult> NewComment([FromBody] CreateCommentRequest request)
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);
        var comment = new Comments
        {
            PostId = request.PostId,
            UserId = userId,
            CommentText = request.CommentText,
            CreatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);

        try
        {
            // Save the comment to the database
            await _context.SaveChangesAsync();

            // Add an entry to UserInteraction for this comment
            var interaction = new UserInteraction
            {
                UserId = userId,
                PostId = request.PostId,
                InteractionTime = DateTime.UtcNow,
                InteractionType = "comment"
            };

            _context.UserInteraction.Add(interaction);

            // Save the interaction to the database
            await _context.SaveChangesAsync();

            // Return the created comment details
            return CreatedAtAction(nameof(NewComment), new { id = comment.Id }, comment);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return StatusCode(500, new { message = "An error occurred while creating the comment." });
        }
    }

    [HttpPost("listInteractions")]
    public async Task<IActionResult> ListInteractions()
    {
        var interactions = await _context.UserInteraction.ToListAsync();
        return Ok(interactions);
    }
    [HttpGet("getRecommendations")]
    public async Task<IActionResult> GetRecommendations()
    {
        var token = Request.Cookies["auth_token"];
        if (string.IsNullOrEmpty(token))
        {
            return Unauthorized("Token not found. Please log in.");
        }

        var userId = _jwtVerifyer.ExtractUserId(token);
        // Get the user's PreferredTags
        var user = await _context.Users
            .Where(u => u.Id == userId)
            .FirstOrDefaultAsync();

        List<Posts> allPosts = null;

        if (user == null || user.PreferredTags == null || !user.PreferredTags.Any())
        {
            // Fetch all posts that the user has NOT interacted with
            allPosts = await _context.Posts
                .Where(post => !_context.UserInteraction
                .Any(ui => ui.UserId == userId && ui.PostId == post.Id))
                .Take(100)
                .ToListAsync();
        }
        else
        {
            var preferredTags = user.PreferredTags;

            // Fetch 20 posts that the user hasn't interacted with and that match at least one preferred tag
            allPosts = await _context.Posts
                .Where(post => !_context.UserInteraction
                    .Any(ui => ui.UserId == userId && ui.PostId == post.Id) &&
                    post.Tags.Any(tag => preferredTags.Contains(tag))) // Check for tag overlap
                .Take(20)
                .ToListAsync();

        }

        var recommendedPosts = new List<PostRecommendation>();

        foreach (var post in allPosts)
        {
            int score = 0;

            // Boost score if post has recent interactions from other users
            var recentInteractions = await _context.UserInteraction
                .Where(ui => ui.PostId == post.Id && ui.InteractionTime > DateTime.Now.AddDays(-7))
                .CountAsync();
            score += recentInteractions * 2;

            // Boost score if post is recent
            var postAgeInDays = (DateTime.Now - post.CreatedAt).TotalDays;
            if (postAgeInDays <= 1)
                score += 10; // Boost for posts created in the last day
            else if (postAgeInDays <= 7)
                score += 5; // Boost for posts created in the last week

            // Adjust score based on engagement (like count)
            var likeCount = await _context.Likes
                .Where(l => l.PostId == post.Id)
                .CountAsync();
            score += likeCount;

            // Adjust score based on comment count
            var commentCount = await _context.Comments
                .Where(c => c.PostId == post.Id)
                .CountAsync();
            score += commentCount * 2; // Boost score for comments
            
            var tags = user?.PreferredTags ?? new List<string>();

            if (tags != null && tags.Any() && post.Tags != null && post.Tags.Any())
            {
                foreach (var tag in post.Tags)
                {
                    if (tags.Contains(tag))
                    {
                        score += 5;
                    }
                }
            }

            // Add post to recommendation list if it has a minimum score
            if (score > 5)
            {
                recommendedPosts.Add(new PostRecommendation
                {
                    Post = post,
                    Score = score
                });

            }
            if (recommendedPosts.Count() >= 10)
            {
                break;
            }
        }

        // Sort recommended posts by score descending, then by creation date
        var sortedPosts = recommendedPosts
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Post.CreatedAt)
            .Select(r => r.Post) // Select the actual post to return
            .ToList();

        return Ok(sortedPosts);
    }

}

// Request DTO for login
public class LoginRequest
{
    public string Username { get; set; }
    public string Password { get; set; }
}

public class UserCreate
{
    public string Username { get; set; }
    public string Password { get; set; }
    public string Mail { get; set; }
}

// Request DTO for editing the Bio
public class EditBioRequest
{
    public string Bio { get; set; }
}

public class CreatePostRequest
{
    public string Content { get; set; }
    public string? ImageBase64 { get; set; } // Optional field for image URL
    public List<string>? Tags { get; set; }
}

public class PostDetails
{
    public Posts Posts { get; set; }
    public int Likes { get; set; }
    public List<Comments> Comments { get; set; }
}

public class CreateCommentRequest
{
    public int PostId { get; set; }
    public string CommentText { get; set; }
}

public class PostRecommendation
{
    public Posts Post { get; set; }
    public int Score { get; set; }
}