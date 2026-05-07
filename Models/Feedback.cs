using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Balay_Balay_Resort.Models
{
    public class Feedback
    {
        [Key]
        public int Feedback_ID { get; set; }

        public int User_ID { get; set; }

        [ForeignKey(nameof(User_ID))]
        public User? User { get; set; }

        public string? Comment { get; set; }

        [Column(TypeName = "decimal(3,1)")]
        public decimal ReviewRate { get; set; }

        public int Property_ID { get; set; }

        [ForeignKey(nameof(Property_ID))]
        public Property? Property { get; set; }
    }
}
