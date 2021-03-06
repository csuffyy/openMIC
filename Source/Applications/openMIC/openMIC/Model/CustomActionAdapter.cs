using System;
using System.ComponentModel.DataAnnotations;

namespace openMIC.Model
{
    public class CustomActionAdapter
    {
        public Guid NodeID
        {
            get;
            set;
        }

        public int ID
        {
            get;
            set;
        }

        [Required]
        [StringLength(200)]
        public string AdapterName
        {
            get;
            set;
        }

        [Required]
        public string AssemblyName
        {
            get;
            set;
        }

        [Required]
        public string TypeName
        {
            get;
            set;
        }

        public string ConnectionString
        {
            get;
            set;
        }

        public int LoadOrder
        {
            get;
            set;
        }

        public bool Enabled
        {
            get;
            set;
        }

        public DateTime CreatedOn
        {
            get;
            set;
        }

        [Required]
        [StringLength(200)]
        public string CreatedBy
        {
            get;
            set;
        }

        public DateTime UpdatedOn
        {
            get;
            set;
        }

        [Required]
        [StringLength(200)]
        public string UpdatedBy
        {
            get;
            set;
        }
    }
}