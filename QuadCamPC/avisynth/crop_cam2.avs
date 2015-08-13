#
# Extract cam2 from full image
# Run script using one of the following supported programs:
#  https://en.wikipedia.org/wiki/AviSynth#AviSynth_compatible_programs
#

# Load in global variables (src_file)
Import("globals.avs")

# Load image, one frame only
src_img = ImageSource(src_file, 0, 0, 1)

# Crop the top right image
cc2 = Crop(src_img, 640, 0, 0, -(480+64))
ImageWriter(cc2, file="cam2", type="png")
