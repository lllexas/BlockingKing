@echo off
cd /d %~dp0
python -c "import matplotlib; import numpy; import scipy; print('matplotlib', matplotlib.__version__); print('numpy', numpy.__version__); print('scipy', scipy.__version__)"
