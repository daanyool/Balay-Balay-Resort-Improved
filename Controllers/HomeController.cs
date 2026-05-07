using Balay_Balay_Resort.Data;
using Balay_Balay_Resort.Models;
using Balay_Balay_Resort.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;

namespace BalayBalayResort.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _context;

    public HomeController(AppDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet]
    [Route("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.User_ID == userId.Value);

        if (user == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var recommendedProperties = await _context.Properties
           .Where(p => p.IsActive)
           .Include(p => p.Feedbacks)
           .OrderByDescending(p => p.Feedbacks.Any()
               ? p.Feedbacks.Average(f => f.ReviewRate)
               : 0)
           .Take(4)
           .ToListAsync();

        var recentBookings = await _context.Bookings
            .Include(b => b.Property)
            .Where(b => b.User_ID == userId.Value)
            .OrderByDescending(b => b.Booking_ID)
            .Take(5)
            .ToListAsync();

        ViewBag.FullName = $"{user.FirstName} {user.LastName}".Trim();
        ViewBag.UserType = user.UserType;
        ViewBag.ProfileImagePath = string.IsNullOrWhiteSpace(user.ProfileImagePath)
        ? ""
        : user.ProfileImagePath;

        ViewBag.RecommendedProperties = recommendedProperties;
        ViewBag.RecentBookings = recentBookings;

        return View("Index");
    }

    [HttpGet]
    [Route("browse-properties")]
    public async Task<IActionResult> BrowseProperties(string search = "")
    {
        var query = _context.Properties
            .Where(p => p.IsActive)
            .Include(p => p.Feedbacks)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p =>
                (p.Property_Name != null && p.Property_Name.ToLower().Contains(s)) ||
                p.UnitNumber.ToString().Contains(s) ||
                (p.Description != null && p.Description.ToLower().Contains(s)));
        }

        ViewBag.SearchQuery = search;

        var properties = await query
            .OrderByDescending(p => p.Property_ID)
            .ToListAsync();

        return View(properties);
    }

    [HttpGet]
    [Route("property/{id}")]
    public async Task<IActionResult> PropertyDetail(int id)
    {
        var property = await _context.Properties
            .Include(p => p.Feedbacks)
                .ThenInclude(f => f.User)
            .Include(p => p.Amenity_Properties)
                .ThenInclude(ap => ap.Amenity)
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null || !property.IsActive)
        {
            return NotFound();
        }

        return View(property);
    }

    [HttpGet]
    [Route("booking/{id}")]
    public async Task<IActionResult> Booking(int id)
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null || !property.IsActive)
        {
            return NotFound();
        }

        return View(property);
    }

    [HttpPost]
    [Route("property/{id}/feedback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFeedback(int id, decimal ReviewRate, string? Comment)
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var propertyExists = await _context.Properties
            .AnyAsync(p => p.Property_ID == id);

        if (!propertyExists)
        {
            return NotFound();
        }

        if (ReviewRate < 1 || ReviewRate > 5)
        {
            TempData["Error"] = "Please select a rating.";
            return RedirectToAction(nameof(PropertyDetail), new { id });
        }

        var feedback = new Feedback
        {
            Property_ID = id,
            User_ID = userId.Value,
            ReviewRate = ReviewRate,
            Comment = Comment
        };

        _context.Feedbacks.Add(feedback);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(PropertyDetail), new { id });
    }

    [HttpGet]
    [Route("booking/{id}/check-availability")]
    public async Task<IActionResult> CheckAvailability(int id, DateTime checkInDate, DateTime checkOutDate)
    {
        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null)
        {
            return Json(new
            {
                available = false,
                message = "Unit not found."
            });
        }

        if (checkInDate == default || checkOutDate == default)
        {
            return Json(new
            {
                available = false,
                message = "Please select check-in and check-out dates."
            });
        }

        if (checkOutDate <= checkInDate)
        {
            return Json(new
            {
                available = false,
                message = "Check-out date must be after check-in date."
            });
        }

        var overlappingBookings = await _context.Bookings
            .Include(b => b.Transaction)
            .Where(b =>
                b.Property_ID == id &&
                b.Status != "Cancelled" &&
                checkInDate <= b.CheckOutDate &&
                checkOutDate >= b.CheckInDate)
            .ToListAsync();

        bool hasPaidBooking = overlappingBookings.Any(b =>
            b.Status == "Confirmed" ||
            b.Transaction != null && b.Transaction.Status == "Completed");

        if (hasPaidBooking)
        {
            return Json(new
            {
                available = false,
                message = "This unit is not available for the selected dates because it is already paid/booked."
            });
        }

        bool hasUnpaidBooking = overlappingBookings.Any(b =>
            b.Status == "Pending" ||
            b.Transaction == null ||
            b.Transaction.Status == "Pending");

        if (hasUnpaidBooking)
        {
            return Json(new
            {
                available = true,
                message = "This unit has an unpaid pending booking. It will be automatically cancelled when you confirm your booking."
            });
        }

        return Json(new
        {
            available = true,
            message = "This unit is available for your selected dates."
        });
    }

    [HttpPost]
    [Route("booking/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Booking(
    int id,
    DateTime CheckInDate,
    DateTime CheckOutDate,
    int NumberOfGuests,
    string PaymentMode,
    decimal AmountGiven)
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null)
        {
            return NotFound();
        }

        if (!property.IsActive)
        {
            TempData["Error"] = "This unit is no longer available for booking.";
            return RedirectToAction(nameof(BrowseProperties));
        }

        if (CheckInDate == default || CheckOutDate == default)
        {
            TempData["Error"] = "Please select check-in and check-out dates.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        if (CheckOutDate <= CheckInDate)
        {
            TempData["Error"] = "Check-out date must be after check-in date.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        if (NumberOfGuests < 1 || NumberOfGuests > property.GuestCapacity)
        {
            TempData["Error"] = $"Maximum guests allowed is {property.GuestCapacity}.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        int nights = (CheckOutDate - CheckInDate).Days;

        if (nights <= 0)
        {
            TempData["Error"] = "Invalid booking dates.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        decimal serviceFee = 0;
        decimal totalAmount = (property.Amount * nights) + serviceFee;

        if (AmountGiven < totalAmount)
        {
            TempData["Error"] = "Insufficient payment amount.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        var overlappingBookings = await _context.Bookings
            .Include(b => b.Transaction)
            .Where(b =>
                b.Property_ID == id &&
                b.Status != "Cancelled" &&
                CheckInDate <= b.CheckOutDate &&
                CheckOutDate >= b.CheckInDate)
            .ToListAsync();

        bool hasPaidBooking = overlappingBookings.Any(b =>
            b.Status == "Confirmed" ||
            b.Transaction != null && b.Transaction.Status == "Completed");

        if (hasPaidBooking)
        {
            TempData["Error"] = "This unit is already paid/booked for the selected dates.";
            return RedirectToAction(nameof(Booking), new { id });
        }

        var unpaidBookings = overlappingBookings
            .Where(b =>
                b.Status == "Pending" ||
                b.Transaction == null ||
                b.Transaction.Status == "Pending")
            .ToList();

        foreach (var oldBooking in unpaidBookings)
        {
            oldBooking.Status = "Cancelled";

            if (oldBooking.Transaction != null)
            {
                oldBooking.Transaction.Status = "Cancelled";
            }
        }

        bool isCash = string.Equals(PaymentMode, "Cash", StringComparison.OrdinalIgnoreCase);

        var booking = new Booking
        {
            User_ID = userId.Value,
            Property_ID = property.Property_ID,
            CheckInDate = CheckInDate,
            CheckOutDate = CheckOutDate,
            NumberOfGuest = NumberOfGuests,
            TotalAmount = totalAmount,
            Status = isCash ? "Pending" : "Confirmed"
        };

        _context.Bookings.Add(booking);
        await _context.SaveChangesAsync();

        var transaction = new Transaction
        {
            Booking_ID = booking.Booking_ID,
            PaymentMode = string.IsNullOrWhiteSpace(PaymentMode) ? "Cash" : PaymentMode,
            ReferenceNum = isCash ? "N/A" : "REF-" + DateTime.Now.ToString("yyyyMMddHHmmss"),
            Date = DateOnly.FromDateTime(DateTime.Now),
            Status = isCash ? "Pending" : "Completed"
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        TempData["Success"] = unpaidBookings.Any()
            ? "Booking completed. Previous unpaid overlapping booking was automatically cancelled."
            : "Booking completed successfully.";

        return RedirectToAction(nameof(MyBookings));
    }

    [HttpPost]
    [Route("booking/{id}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelBooking(int id)
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var booking = await _context.Bookings
            .Include(b => b.Transaction)
            .FirstOrDefaultAsync(b => b.Booking_ID == id && b.User_ID == userId.Value);

        if (booking == null)
        {
            return NotFound();
        }

        booking.Status = "Cancelled";

        if (booking.Transaction != null)
        {
            booking.Transaction.Status = "Cancelled";
        }
        else
        {
            var transaction = new Transaction
            {
                Booking_ID = booking.Booking_ID,
                PaymentMode = "N/A",
                ReferenceNum = "N/A",
                Date = DateOnly.FromDateTime(DateTime.Now),
                Status = "Cancelled"
            };

            _context.Transactions.Add(transaction);
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Booking cancelled successfully.";

        return RedirectToAction(nameof(MyBookings));
    }

    [HttpGet]
    [Route("my-bookings")]
    public async Task<IActionResult> MyBookings(string search = "")
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var bookingsQuery = _context.Bookings
            .Include(b => b.Property)
            .Include(b => b.Transaction)
            .Where(b => b.User_ID == userId.Value)
            .AsQueryable();

        var transactionsQuery = _context.Transactions
            .Include(t => t.Booking)
                .ThenInclude(b => b.Property)
            .Where(t => t.Booking != null && t.Booking.User_ID == userId.Value)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            string sLower = s.ToLower();

            // Try parsing as a plain number, OR strip "BK"/"TXN" prefix (e.g. "BK001" -> 1)
            bool isNumber = int.TryParse(s, out int searchId);
            if (!isNumber)
            {
                var stripped = System.Text.RegularExpressions.Regex.Replace(s, @"[^\d]", "");
                if (!string.IsNullOrWhiteSpace(stripped) && int.TryParse(stripped, out int idFromPrefix))
                {
                    searchId = idFromPrefix;
                    isNumber = true;
                }
            }

            bookingsQuery = bookingsQuery.Where(b =>
                (b.Status != null && b.Status.ToLower().Contains(sLower)) ||
                (b.Property != null && b.Property.Property_Name != null && b.Property.Property_Name.ToLower().Contains(sLower)) ||
                (b.Transaction != null && b.Transaction.PaymentMode != null && b.Transaction.PaymentMode.ToLower().Contains(sLower)) ||
                (isNumber && b.Booking_ID == searchId)
            );

            transactionsQuery = transactionsQuery.Where(t =>
                (t.PaymentMode != null && t.PaymentMode.ToLower().Contains(sLower)) ||
                (t.ReferenceNum != null && t.ReferenceNum.ToLower().Contains(sLower)) ||
                (t.Status != null && t.Status.ToLower().Contains(sLower)) ||
                (isNumber && t.Transaction_ID == searchId) ||
                (isNumber && t.Booking_ID == searchId)
            );
        }

        var vm = new MyBookingsViewModel
        {
            Bookings = await bookingsQuery
                .OrderByDescending(b => b.Booking_ID)
                .ToListAsync(),

            Transactions = await transactionsQuery
                .OrderByDescending(t => t.Transaction_ID)
                .ToListAsync(),

            SearchQuery = search
        };

        return View(vm);
    }

    [HttpGet]
    [Route("edit-profile")]
    public async Task<IActionResult> EditProfile()
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.User_ID == userId.Value);

        if (user == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var vm = new EditProfileViewModel
        {
            FullName = $"{user.FirstName} {user.LastName}".Trim(),
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            ProfileImagePath = user.ProfileImagePath
        };

        return View(vm);
    }

    [HttpPost]
    [Route("edit-profile")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProfile(EditProfileViewModel model, IFormFile? ProfilePicture)
    {
        int? userId = GetCurrentUserId();

        if (userId == null)
        {
            return RedirectToAction("Login", "Account");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.User_ID == userId.Value);

        if (user == null)
        {
            return RedirectToAction("Login", "Account");
        }

        var nameParts = model.FullName.Trim().Split(' ', 2);

        user.FirstName = nameParts.Length > 0 ? nameParts[0] : "";
        user.LastName = nameParts.Length > 1 ? nameParts[1] : "";
        user.Email = model.Email;
        user.PhoneNumber = model.PhoneNumber;

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            user.Password = model.Password;
        }

        if (ProfilePicture != null && ProfilePicture.Length > 0)
        {
            string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "profiles");

            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await ProfilePicture.CopyToAsync(fileStream);
            }

            user.ProfileImagePath = "/images/profiles/" + uniqueFileName;
        }

        await _context.SaveChangesAsync();

        model.SuccessMessage = "Profile updated successfully!";
        model.Password = null;
        model.ConfirmPassword = null;
        model.ProfileImagePath = user.ProfileImagePath;

        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private int? GetCurrentUserId()
    {
        int? sessionUserId = HttpContext.Session.GetInt32("User_ID");

        if (sessionUserId != null)
        {
            return sessionUserId;
        }

        int? sessionUserIdAlt = HttpContext.Session.GetInt32("UserId");

        if (sessionUserIdAlt != null)
        {
            return sessionUserIdAlt;
        }

        string? claimUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(claimUserId, out int userId))
        {
            return userId;
        }

        return null;
    }
}