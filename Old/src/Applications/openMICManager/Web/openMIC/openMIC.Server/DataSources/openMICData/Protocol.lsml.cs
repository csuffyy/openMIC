﻿using Microsoft.LightSwitch;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System;

namespace LightSwitchApplication
{
    public partial class Protocol
    {
        partial void Acronym_Validate(EntityValidationResultsBuilder results)
        {
            openMICDataService.ValidateAcronym(Acronym, results);
        }
    }
}