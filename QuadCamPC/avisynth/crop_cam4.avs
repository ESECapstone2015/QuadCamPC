#
# Extract cam4 from full image
# Run script using one of the following supported programs:
#  https://en.wikipedia.org/wiki/AviSynth#AviSynth_compatible_programs
#

# Load in global variables (src_file)
Import("globals.avs")

# Load image, one frame only
src_img = ImageSource(src_file, 0, 0, 1)

# Crop the bottom left image
cc4 = Crop(src_img, 0, 480, -640, -64)
ImageWriter(cc4, file="cam4", type="png")
