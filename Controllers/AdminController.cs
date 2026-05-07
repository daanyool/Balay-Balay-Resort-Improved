using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Balay_Balay_Resort.Data;
using Balay_Balay_Resort.Models;
using Balay_Balay_Resort.ViewModels;

namespace Balay_Balay_Resort.Controllers;

[Route("admin")]
public class AdminController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public AdminController(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    [Route("")]
    [Route("dashboard")]
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var recentBookingEntities = await _context.Bookings
            .Include(b => b.Property)
            .OrderByDescending(b => b.CheckInDate)
            .Take(5)
            .ToListAsync();

        var recentBookings = recentBookingEntities
            .Select(b => new AdminBookingRow
            {
                PropertyName = b.Property?.Property_Name ?? "Unknown Property",
                Date = b.CheckInDate.ToString("yyyy-MM-dd"),
                Status = b.Status ?? "Pending"
            })
            .ToList();

        var recentTransactionEntities = await _context.Transactions
            .Include(t => t.Booking)
            .OrderByDescending(t => t.Date)
            .Take(5)
            .ToListAsync();

        var recentTransactions = recentTransactionEntities
            .Select(t => new AdminTransactionRow
            {
                TransactionId = "TXN" + t.Transaction_ID.ToString("D3"),
                PaymentMethod = t.PaymentMode ?? "Unknown",
                Amount = t.Booking?.TotalAmount ?? 0,
                Date = t.Date.ToString("yyyy-MM-dd")
            })
            .ToList();

        var completedTransactionsForRevenue = await _context.Transactions
            .Include(t => t.Booking)
            .Where(t => t.Status == "Completed")
            .ToListAsync();

        var totalRevenue = completedTransactionsForRevenue
            .Sum(t => t.Booking?.TotalAmount ?? 0);

        var vm = new AdminDashboardViewModel
        {
            TotalUsers = await _context.Users.CountAsync(),
            TotalProperties = await _context.Properties.CountAsync(),
            ActiveBookings = await _context.Bookings.CountAsync(b => b.Status != "Cancelled"),
            TotalRevenue = totalRevenue,
            RecentBookings = recentBookings,
            RecentTransactions = recentTransactions
        };

        return View(vm);
    }

    // ===================== PROPERTIES PAGE =====================
    [Route("properties")]
    [HttpGet]
    public async Task<IActionResult> Properties(
        string search = "",
        decimal? minPrice = null,
        decimal? maxPrice = null,
        int? guests = null,
        int? bedrooms = null,
        int? bathrooms = null,
        double? minRating = null,
        List<string>? amenities = null,
        string sort = "")
    {
        var query = _context.Properties
            .Include(p => p.Feedbacks)
            .Include(p => p.Amenity_Properties)
                .ThenInclude(ap => ap.Amenity)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            bool isActiveFilter   = s.Equals("active",   StringComparison.OrdinalIgnoreCase);
            bool isInactiveFilter = s.Equals("inactive", StringComparison.OrdinalIgnoreCase);

            if (isActiveFilter)
            {
                query = query.Where(p => p.IsActive);
            }
            else if (isInactiveFilter)
            {
                query = query.Where(p => !p.IsActive);
            }
            else
            {
                query = query.Where(p =>
                    p.Property_Name.Contains(s) ||
                    p.UnitNumber.ToString().Contains(s) ||
                    p.Description.Contains(s) ||
                    p.Amenity_Properties.Any(ap => ap.Amenity.Amenity_Name.Contains(s))
                );
            }
        }

        if (minPrice.HasValue)  query = query.Where(p => p.Amount >= minPrice.Value);
        if (maxPrice.HasValue)  query = query.Where(p => p.Amount <= maxPrice.Value);
        if (guests.HasValue    && guests.Value    > 0) query = query.Where(p => p.GuestCapacity >= guests.Value);
        if (bedrooms.HasValue  && bedrooms.Value  > 0) query = query.Where(p => p.BedNum  >= bedrooms.Value);
        if (bathrooms.HasValue && bathrooms.Value > 0) query = query.Where(p => p.BathNum >= bathrooms.Value);

        if (amenities != null && amenities.Any())
        {
            foreach (var amenity in amenities.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                var a = amenity;
                query = query.Where(p => p.Amenity_Properties.Any(ap => ap.Amenity.Amenity_Name == a));
            }
        }

        var list = await query.ToListAsync();

        if (minRating.HasValue && minRating.Value > 0)
        {
            list = list
                .Where(p => p.Feedbacks.Any() && p.Feedbacks.Average(f => (double)f.ReviewRate) >= minRating.Value)
                .ToList();
        }

        // Sort
        list = sort switch
        {
            "price_asc"   => list.OrderBy(p => p.Amount).ToList(),
            "price_desc"  => list.OrderByDescending(p => p.Amount).ToList(),
            "rating"      => list.OrderByDescending(p =>
                                p.Feedbacks.Any() ? p.Feedbacks.Average(f => (double)f.ReviewRate) : 0).ToList(),
            "newest"      => list.OrderByDescending(p => p.Property_ID).ToList(),
            _             => list.OrderBy(p => p.UnitNumber).ToList()  // default: by unit number
        };

        ViewBag.MinPrice  = minPrice;
        ViewBag.MaxPrice  = maxPrice;
        ViewBag.Guests    = guests;
        ViewBag.Bedrooms  = bedrooms;
        ViewBag.Bathrooms = bathrooms;
        ViewBag.MinRating = minRating;
        ViewBag.Amenities = amenities ?? new List<string>();
        ViewBag.Sort      = sort;

        var vm = new AdminPropertiesViewModel
        {
            Properties  = list,
            SearchQuery = search,
            NewProperty = new Property()
        };

        return View(vm);
    }

    // ===================== ADD PROPERTY =====================
    [Route("properties/add")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> AddProperty(
        string Name,
        int UnitNumber,
        decimal PricePerNight,
        int Guests,
        int Beds,
        int Baths,
        string? Description,
        string? Amenities,
        List<IFormFile>? ImageFiles)
    {
        if (string.IsNullOrWhiteSpace(Name) ||
            UnitNumber <= 0 ||
            PricePerNight <= 0 ||
            Guests <= 0 ||
            Beds <= 0 ||
            Baths <= 0)
        {
            TempData["Error"] = "Please fill in all required fields correctly.";
            return RedirectToAction(nameof(Properties));
        }

        var imagePath = await SavePropertyImage(ImageFiles);

        var property = new Property
        {
            Property_Name = Name.Trim(),
            UnitNumber = UnitNumber,
            Amount = PricePerNight,
            AmountUnit = "night",
            GuestCapacity = Guests,
            BedNum = Beds,
            BathNum = Baths,
            Description = Description ?? "",
            PropertyImagePath = imagePath ?? "/images/bg-login.png"
        };

        _context.Properties.Add(property);
        await _context.SaveChangesAsync();

        await SaveAmenities(property.Property_ID, Amenities);

        TempData["Success"] = "Property unit added successfully.";
        return RedirectToAction(nameof(Properties));
    }

    // ===================== EDIT PROPERTY =====================
    [Route("properties/edit/{id}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)]
    public async Task<IActionResult> EditProperty(
        int id,
        string Name,
        int UnitNumber,
        decimal PricePerNight,
        int Guests,
        int Beds,
        int Baths,
        string? Description,
        string? Amenities,
        List<IFormFile>? ImageFiles)
    {
        var property = await _context.Properties
            .Include(p => p.Amenity_Properties)
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(Name) ||
            UnitNumber <= 0 ||
            PricePerNight <= 0 ||
            Guests <= 0 ||
            Beds <= 0 ||
            Baths <= 0)
        {
            TempData["Error"] = "Please fill in all required fields correctly.";
            return RedirectToAction(nameof(Properties));
        }

        property.Property_Name = Name.Trim();
        property.UnitNumber = UnitNumber;
        property.Amount = PricePerNight;
        property.AmountUnit = "night";
        property.GuestCapacity = Guests;
        property.BedNum = Beds;
        property.BathNum = Baths;
        property.Description = Description ?? "";

        var newImagePath = await SavePropertyImage(ImageFiles);

        if (!string.IsNullOrWhiteSpace(newImagePath))
        {
            property.PropertyImagePath = newImagePath;
        }

        await _context.SaveChangesAsync();
        await UpdateAmenities(property.Property_ID, Amenities);

        TempData["Success"] = "Property unit updated successfully.";
        return RedirectToAction(nameof(Properties));
    }

    // ===================== TOGGLE PROPERTY STATUS (soft delete) =====================
    [Route("properties/toggle/{id}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePropertyStatus(int id)
    {
        var property = await _context.Properties
            .FirstOrDefaultAsync(p => p.Property_ID == id);

        if (property == null)
            return NotFound();

        property.IsActive = !property.IsActive;
        await _context.SaveChangesAsync();

        TempData["Success"] = property.IsActive
            ? $"Unit \"{property.Property_Name}\" has been activated."
            : $"Unit \"{property.Property_Name}\" has been deactivated.";

        return RedirectToAction(nameof(Properties));
    }

    // ===================== TRANSACTIONS PAGE =====================
    [Route("transactions")]
    [HttpGet]
    public async Task<IActionResult> Transactions(
        string search = "",
        string status = "",
        string paymentMethod = "",
        DateTime? fromDate = null,
        DateTime? toDate = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        string sort = "")
    {
        var transactionsQuery = _context.Transactions
            .Include(t => t.Booking)
                .ThenInclude(b => b.Property)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            transactionsQuery = transactionsQuery.Where(t => t.Status == status);

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            transactionsQuery = transactionsQuery.Where(t => t.PaymentMode == paymentMethod);

        if (fromDate.HasValue)
        {
            var f = DateOnly.FromDateTime(fromDate.Value);
            transactionsQuery = transactionsQuery.Where(t => t.Date >= f);
        }

        if (toDate.HasValue)
        {
            var to = DateOnly.FromDateTime(toDate.Value);
            transactionsQuery = transactionsQuery.Where(t => t.Date <= to);
        }

        var transactions = await transactionsQuery
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Transaction_ID)
            .ToListAsync();

        var items = transactions.Select(t => new TransactionItem
        {
            TransactionId = "TXN" + t.Transaction_ID.ToString("D3"),
            BookingId = "BK" + t.Booking_ID.ToString("D3"),
            PropertyName = t.Booking?.Property?.Property_Name ?? "Unknown Property",
            Amount = t.Booking?.TotalAmount ?? 0,
            Method = t.PaymentMode ?? "Unknown",
            Date = t.Date.ToString("yyyy-MM-dd"),
            Reference = t.ReferenceNum ?? "N/A",
            Status = t.Status ?? "Pending"
        });

        if (minAmount.HasValue)
            items = items.Where(t => t.Amount >= minAmount.Value);

        if (maxAmount.HasValue)
            items = items.Where(t => t.Amount <= maxAmount.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            items = items.Where(t =>
                (t.TransactionId ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.BookingId ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.PropertyName ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.Method ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.Reference ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.Status ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (t.Date ?? "").Contains(s, StringComparison.OrdinalIgnoreCase) ||
                t.Amount.ToString().Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var list = items.ToList();

        // Sort
        list = sort switch
        {
            "oldest"      => list.OrderBy(t => t.Date).ThenBy(t => t.TransactionId).ToList(),
            "amount_desc" => list.OrderByDescending(t => t.Amount).ToList(),
            "amount_asc"  => list.OrderBy(t => t.Amount).ToList(),
            _             => list  // default: newest (already ordered by date desc)
        };

        ViewBag.Status        = status;
        ViewBag.PaymentMethod = paymentMethod;
        ViewBag.FromDate      = fromDate;
        ViewBag.ToDate        = toDate;
        ViewBag.MinAmount     = minAmount;
        ViewBag.MaxAmount     = maxAmount;
        ViewBag.Sort          = sort;

        var vm = new AdminTransactionsViewModel
        {
            TotalTransactions = list.Count,
            TotalRevenue = list
                .Where(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount),
            Completed = list.Count(t =>
                string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
            Cancelled = list.Count(t =>
                string.Equals(t.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)),
            Pending = list.Count(t =>
                string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase)),
            Transactions = list,
            SearchQuery = search
        };

        return View(vm);
    }

    // ===================== EDIT TRANSACTION =====================
    [Route("transactions/edit/{id}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTransaction(
        int id,
        decimal Amount,
        string PaymentMode,
        DateTime Date,
        string Status,
        string? ReferenceNum)
    {
        var transaction = await _context.Transactions
            .Include(t => t.Booking)
            .FirstOrDefaultAsync(t => t.Transaction_ID == id);

        if (transaction == null)
        {
            return NotFound();
        }

        bool isCash = string.Equals(PaymentMode, "Cash", StringComparison.OrdinalIgnoreCase);

        transaction.PaymentMode = string.IsNullOrWhiteSpace(PaymentMode)
            ? "Unknown"
            : PaymentMode;

        transaction.Date = DateOnly.FromDateTime(Date);

        transaction.Status = string.IsNullOrWhiteSpace(Status)
            ? "Pending"
            : Status;

        transaction.ReferenceNum = isCash
            ? "N/A"
            : string.IsNullOrWhiteSpace(ReferenceNum)
                ? transaction.ReferenceNum ?? "REF-" + DateTime.Now.ToString("yyyyMMddHHmmss")
                : ReferenceNum;

        if (transaction.Booking != null)
        {
            transaction.Booking.TotalAmount = Amount;

            if (string.Equals(transaction.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                transaction.Booking.Status = "Confirmed";
            }
            else if (string.Equals(transaction.Status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                transaction.Booking.Status = "Pending";
            }
            else if (string.Equals(transaction.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            {
                transaction.Booking.Status = "Cancelled";
            }
        }

        await _context.SaveChangesAsync();

        TempData["Success"] = "Transaction updated successfully.";
        return RedirectToAction(nameof(Transactions));
    }

    // ===================== REPORTS PAGE =====================
    [Route("reports")]
    [HttpGet]
    public async Task<IActionResult> Reports()
    {
        var bookings = await _context.Bookings
            .Include(b => b.Property)
            .ToListAsync();

        var transactions = await _context.Transactions
            .Include(t => t.Booking)
            .ToListAsync();

        var completedTransactions = transactions
            .Where(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalRevenue = completedTransactions.Sum(t => t.Booking?.TotalAmount ?? 0);
        var totalBookings = bookings.Count;
        var avgBookingValue = totalBookings > 0 ? bookings.Average(b => b.TotalAmount) : 0;

        var confirmedBookings = bookings.Count(b => string.Equals(b.Status, "Confirmed", StringComparison.OrdinalIgnoreCase));
        var pendingBookings = bookings.Count(b => string.Equals(b.Status, "Pending", StringComparison.OrdinalIgnoreCase));
        var cancelledBookings = bookings.Count(b => string.Equals(b.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));

        var customerSatisfaction = await _context.Feedbacks.AnyAsync()
            ? await _context.Feedbacks.Select(f => (double)f.ReviewRate).AverageAsync()
            : 0;

        var topProperties = bookings
            .Where(b => b.Property != null)
            .GroupBy(b => b.Property!)
            .Select(g => new
            {
                Property = g.Key,
                Revenue = g.Sum(b => b.TotalAmount),
                Bookings = g.Count()
            })
            .OrderByDescending(x => x.Revenue)
            .Take(4)
            .Select((x, index) => new TopProperty
            {
                Rank = index + 1,
                Name = x.Property.Property_Name,
                Location = "",
                Revenue = x.Revenue,
                Bookings = x.Bookings,
                ImageUrl = x.Property.PropertyImagePath
            })
            .ToList();

        var paymentMethods = completedTransactions
            .GroupBy(t => string.IsNullOrWhiteSpace(t.PaymentMode) ? "Unknown" : t.PaymentMode)
            .Select(g => new
            {
                Method = g.Key,
                Transactions = g.Count(),
                Amount = g.Sum(t => t.Booking?.TotalAmount ?? 0)
            })
            .OrderByDescending(x => x.Amount)
            .Select(x => new PaymentMethodRevenue
            {
                Code = string.IsNullOrWhiteSpace(x.Method)
                    ? "NA"
                    : x.Method.Length <= 2 ? x.Method.ToUpper() : x.Method.Substring(0, 2).ToUpper(),
                Name = x.Method ?? "Unknown",
                Transactions = x.Transactions,
                Amount = x.Amount,
                Percentage = totalRevenue > 0 ? (int)Math.Round((x.Amount / totalRevenue) * 100) : 0,
                Color = "#2563eb"
            })
            .ToList();

        var occupancyRate = totalBookings > 0
            ? (int)Math.Round((decimal)confirmedBookings / totalBookings * 100)
            : 0;

        var vm = new AdminReportsViewModel
        {
            TotalRevenue = totalRevenue,
            TotalBookings = totalBookings,
            AvgBookingValue = avgBookingValue,
            OccupancyRate = occupancyRate,
            ConfirmedBookings = confirmedBookings,
            PendingBookings = pendingBookings,
            CancelledBookings = cancelledBookings,
            RevenueGrowthPct = 0,
            BookingRatePct = 0,
            CustomerSatisfaction = customerSatisfaction,
            TopProperties = topProperties,
            PaymentMethods = paymentMethods
        };

        return View(vm);
    }

    // ===================== IMAGE SAVE HELPER =====================
    // Saves ALL uploaded images and returns comma-separated paths
    private async Task<string?> SavePropertyImage(List<IFormFile>? imageFiles)
    {
        if (imageFiles == null || !imageFiles.Any())
            return null;

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var uploadsFolder = Path.Combine(_environment.WebRootPath, "images", "properties");

        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var savedPaths = new List<string>();

        foreach (var file in imageFiles)
        {
            if (file == null || file.Length == 0) continue;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext)) continue;

            var fileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            savedPaths.Add($"/images/properties/{fileName}");
        }

        return savedPaths.Any() ? string.Join(",", savedPaths) : null;
    }

    // ===================== SAVE AMENITIES HELPER =====================
    private async Task SaveAmenities(int propertyId, string? amenities)
    {
        if (string.IsNullOrWhiteSpace(amenities))
        {
            return;
        }

        var amenityNames = amenities
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var amenityName in amenityNames)
        {
            var amenity = await _context.Amenities
                .FirstOrDefaultAsync(a => a.Amenity_Name == amenityName);

            if (amenity == null)
            {
                amenity = new Amenity
                {
                    Amenity_Name = amenityName
                };

                _context.Amenities.Add(amenity);
                await _context.SaveChangesAsync();
            }

            var alreadyLinked = await _context.Amenities_Properties
                .AnyAsync(ap => ap.Property_ID == propertyId && ap.Amenity_ID == amenity.Amenity_ID);

            if (!alreadyLinked)
            {
                var amenityProperty = new Amenity_Property
                {
                    Property_ID = propertyId,
                    Amenity_ID = amenity.Amenity_ID
                };

                _context.Amenities_Properties.Add(amenityProperty);
            }
        }

        await _context.SaveChangesAsync();
    }

    // ===================== UPDATE AMENITIES HELPER =====================
    private async Task UpdateAmenities(int propertyId, string? amenities)
    {
        var existingAmenities = _context.Amenities_Properties
            .Where(ap => ap.Property_ID == propertyId);

        _context.Amenities_Properties.RemoveRange(existingAmenities);
        await _context.SaveChangesAsync();

        await SaveAmenities(propertyId, amenities);
    }
}
