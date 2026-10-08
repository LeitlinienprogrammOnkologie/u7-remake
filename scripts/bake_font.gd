# Saves godot/fonts/MedievalSharp.ttf as Godot imports it (the settings in its
# .import file) to godot/fonts/MedievalSharp.res, the plain resource UiTheme
# loads, so game runs need no editor import on any machine. Re-run it after
# changing the font's import settings and letting the editor reimport:
#   <godot> --headless --path godot --script ../scripts/bake_font.gd
extends SceneTree

func _init():
	var font: FontFile = load("res://fonts/MedievalSharp.ttf")
	var err = ResourceSaver.save(font, "res://fonts/MedievalSharp.res", ResourceSaver.FLAG_COMPRESS)
	print("fonts/MedievalSharp.res: ", error_string(err))
	quit()
