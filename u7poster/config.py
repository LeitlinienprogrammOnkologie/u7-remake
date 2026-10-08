"""Tunable parameters. Every value here is also written to the Config sheet
so the workbook formulas and a re-run stay in sync."""
from __future__ import annotations

CONFIG = {
    # --- map / images ---
    "map_tiles": 3072,
    "base_px": 2048,
    "up_px": 8192,
    "coarse_px": 4096,
    "poster_w_cm": 100.0,
    "poster_h_cm": 100.0,
    "poster_dpi": 150,
    # --- registration (filled in by registration.py) ---
    "coarse_offset_x": 0.0,
    "coarse_offset_y": 0.0,
    "coarse_scale_x": 4096 / 3072,
    "coarse_scale_y": 4096 / 3072,
    "coarse_residual_px": 0.0,
    # --- print sizes (cm) by priority ---
    "landmark_min_cm_p1": 6.0,
    "landmark_min_cm_p2": 4.0,
    "landmark_min_cm_p3": 2.5,
    "landmark_footprint_factor": 1.3,
    "npc_cm_p1": 3.0,
    "npc_cm_p2": 2.4,
    "npc_cm_p3": 1.8,
    "monster_cm_p1": 3.0,
    "monster_cm_p2": 2.2,
    "monster_cm_p3": 1.5,
    # --- landmark clustering ---
    "landmark_merge_gap_tiles": 24,     # structures closer than this merge into one place
    "landmark_min_tiles": 24,           # min structure tiles for a cluster
    "landmark_npc_margin_tiles": 12,    # NPC homes within this margin of the bbox count
    "cave_mouth_min_tiles": 8,
    "cave_mouth_max_tiles": 64,
    # --- monsters ---
    "monster_group_radius_tiles": 96,   # eggs of one shape closer than this are one spawn group
    # --- defect detection (coarse px) ---
    "defect_min_blob_px": 24,           # blob must be >= this in one dimension (user default was 12; see NOTES)
    "defect_min_area_px": 400,          # ... and cover at least this many px
    "defect_boundary_erode_px": 10,     # ignore disagreements this close to a class boundary (registration slop)
    "defect_open_px": 3,                # morphological opening radius for disagreement masks
    "river_min_fraction": 0.35,         # river blob flagged if less than this fraction reads as water
    "road_min_fraction": 0.25,
    "structure_min_fraction": 0.30,
    "texture_window_px": 512,
    "texture_corr_threshold": 0.55,
    "texture_min_lag_px": 24,
    "text_window_px": 96,
    "text_min_glyphs": 7,
    "crop_px": 1024,
    "crop_margin_px": 150,
    "fix_grid_cols": 4,
    "fix_grid_rows": 3,
}

CONFIG_NOTES = {
    "map_tiles": "world size in tiles (192 chunks x 16 tiles), Exult c_num_tiles",
    "base_px": "pixel width of the base working image",
    "up_px": "pixel width of the upscaled working image",
    "coarse_px": "pixel width of coarse_map.png",
    "poster_w_cm": "printed poster width",
    "poster_h_cm": "printed poster height",
    "poster_dpi": "target print resolution",
    "coarse_offset_x": "coarse px x of tile 0 (fitted by registration)",
    "coarse_offset_y": "coarse px y of tile 0 (fitted by registration)",
    "coarse_scale_x": "coarse px per tile in x (fitted)",
    "coarse_scale_y": "coarse px per tile in y (fitted)",
    "coarse_residual_px": "RMS residual of the block-wise alignment after the fit",
    "landmark_footprint_factor": "print size = max(min cm for priority, footprint cm x factor)",
    "defect_min_blob_px": "disagreement blobs smaller than this (either dimension) are ignored",
    "defect_min_area_px": "disagreement blobs with fewer px are ignored",
    "defect_boundary_erode_px": "class boundary tolerance (registration slop)",
}
