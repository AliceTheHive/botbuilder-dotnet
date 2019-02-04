﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;

namespace Microsoft.Bot.Builder.Dialogs.Flow
{
    /// <summary>
    /// Clear a variable as an action
    /// </summary>
    public class ClearVarStep : IStep
    {
        public ClearVarStep() { }

        /// <summary>
        /// (OPTIONAL) Id of the command
        /// </summary>
        public string Id { get; set; }

        public string Name { get; set; }
        
        public Task<object> Execute(DialogContext dialogContext, CancellationToken cancellationToken)
        {
            var state = dialogContext.DialogState;
            state.Remove(Name);
            return Task.FromResult<object>(null);
        }
    }
}