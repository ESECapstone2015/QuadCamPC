#
# Extract cam1 from full image
# Run script using one of the following supported programs:
#  https://en.wikipedia.org/wiki/AviSynth#AviSynth_compatible_programs
#

# Load in global variables (src_file)
Import("globals.avs")

# Load image, one frame only
src_img = ImageSource(src_file, 0, 0, 1)

# Crop the top left image
cc1 = Crop(src_img, 0, 0, -640, -(480+64))
ImageWriter(cc1, file="cam1", type="png")
