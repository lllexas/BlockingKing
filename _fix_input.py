import re, pathlib 
p = pathlib.Path(r'Assets/Scripts/Stage/UserInputReader.cs') 
c = p.read_text(encoding='utf-8') 
# 1. comment 
c = c.replace('有实体则攻击，无实体则移动', '敌人攻击，箱子推，空地移动') 
