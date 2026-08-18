/*
 * Native AOT entry point. Kept to a single delegation so the whole command
 * surface - public and hidden research alike - lives in DrvCtlApp where it
 * can be exercised without a process boundary.
 */

using DrvCtl.Cli;

return await DrvCtlApp.RunAsync(args);
