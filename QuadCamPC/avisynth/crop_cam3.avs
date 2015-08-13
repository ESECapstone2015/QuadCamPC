#
# Extract cam3 from full image
# Run script using one of the following supported programs:
#  https://en.wikipedia.org/wiki/AviSynth#AviSynth_compatible_programs
#

# Load in global variables (src_file)
Import("globals.avs")

# Load image, one frame only
src_img = ImageSource(src_file, 0, 0, 1)

# Crop the bottom right image
cc3 = Crop(src_img, 640, 480, 0, -64)
ImageWriter(cc3, file="cam3", type="png")
