# WIM and offline registry path

## libwim

The direct publication path works on a copied WIM.

The goal is targeted updates, not mounting and rebuilding the whole image through DISM.

Task 11 used direct libwim mutation successfully.

## Offreg

Required hives are extracted, modified offline, saved, and written back into the copied WIM.

This avoids loading offline hives into the live registry.

## Safety model

The baseline WIM is treated as immutable.

Every experiment creates a disposable copy.

Source WIM and package hashes are checked around important experiments.

## Current limitation

A structurally valid WIM is only the first gate.

Windows recognition and semantic comparison still need to be checked separately.
