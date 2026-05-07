using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Balay_Balay_Resort.Models
{
    public class Property
    {
        [Key]
        public int Property_ID { get; set; }

        public string Property_Name { get; set; }

        public int BedNum { get; set; }

        public int BathNum { get; set; }

        public int GuestCapacity { get; set; }

        public string Description { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public string AmountUnit { get; set; }

        public int UnitNumber { get; set; }

        public string PropertyImagePath { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Amenity_Property> Amenity_Properties { get; set; } = new List<Amenity_Property>();
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<Feedback> Feedbacks { get; set; } = new List<Feedback>();
    }
}
