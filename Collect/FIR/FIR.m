function [Output]=FIR(inputMatrx,inputFilename,N_filter,type)
% function [Output]=FIR(inputMatrx,inputFilename,N_filter,filtermatrix,type)；
% type=1 inputFilename为输入的滤波器系数名称，如XX.mat
% type=2 inputFilename为输入的滤波器系数矩阵
% inputMatrx 输入的DAS一维矩阵
% inputFilename 输入的滤波器系数名称
% N_filter 输入的滤波器阶数
% Output 滤波器输出矩阵
if mod(N_filter,2)==0
    error('N_filter should be odd');
end
if length(inputMatrx(:,1))~=1
    inputMatrx=inputMatrx'
end
delay=fix(N_filter/2);
if type==1
    if (~exist(inputFilename))
    error('inputFilename missing.');
end
Matrx_=ones(1,length(inputMatrx)+(N_filter-1)/2);
fieldstruct=load(inputFilename); 
fields=fieldnames(fieldstruct);
field_data=ones(1,length(fields))
for i=1:1:length(fields)
    field_data=fieldstruct.(fields{i});
end
Matrx_=filter( field_data,1,[inputMatrx zeros(1,delay)]);
Output=Matrx_(delay+1:end);
end
if type==2
    Matrx_=filter( inputFilename,1,[inputMatrx zeros(1,delay)]);
Output=Matrx_(delay+1:end);
end
end