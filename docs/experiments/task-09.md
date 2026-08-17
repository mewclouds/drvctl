# Task 9

## Goal

Determine which mysterious fields are actually required by offline servicing.

## Key result

Deleting complete DriverPackages Version broke package inspection and duplicate Add-Driver with error 1168.

## Other findings

Windows could reconstruct several fields during duplicate servicing.

Some fields could remain absent without breaking package recognition.

This narrowed the required servicing contract but did not prove PnP correctness.
